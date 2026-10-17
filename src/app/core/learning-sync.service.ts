import { Injectable } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';
import { ProgressService, ProgressSnapshot } from './progress.service';

interface ServerSnapshot {
  lessons: Array<{ moduleId: string; lessonId: string; isCompleted: boolean; updatedAt: string }>;
  reviews: Array<{ itemKey: string; title: string; moduleId: string; moduleTitle: string; kind: 'lesson'|'quiz'|'flashcard'; reason: string; isResolved: boolean; createdAt: string; updatedAt: string }>;
  quizAttempts: Array<{ id: string; score: number; total: number; wrongQuestionIds: string[]; completedAt: string }>;
  flashcards: Array<{ flashcardId: string; rating: 'again'|'good'|'easy'; updatedAt: string }>;
}

const SYNCED_ATTEMPTS_KEY = 'academia-dotnet-synced-quiz-attempts-v1';

@Injectable({ providedIn: 'root' })
export class LearningSyncService {
  constructor(private readonly http: HttpClient, private readonly progress: ProgressService, private readonly auth: AuthService) {}

  async sync(): Promise<{ lessons: number; reviews: number; quizzes: number; flashcards: number; replacedLocal: boolean }> {
    const userId = this.auth.currentUser()?.id;
    // Progresso de outra conta não pode ser enviado para esta; as marcas de quiz enviadas também eram dessa conta.
    const replacedLocal = userId ? this.progress.claimFor(userId) : false;
    if (replacedLocal) this.saveSyncedAttempts(new Set<string>());
    const local = this.progress.exportSnapshot();
    let lessonCount = 0, reviewCount = 0, quizCount = 0, flashcardCount = 0;

    for (const deletedId of local.deletedReviewIds) {
      try {
        await firstValueFrom(this.http.delete<void>(`${environment.apiBaseUrl}/learning/reviews/${encodeURIComponent(deletedId)}`));
      } catch (error) {
        if (!(error instanceof HttpErrorResponse) || error.status !== 404) throw error;
      }
    }
    if (local.deletedReviewIds.length) this.progress.clearDeletedReviewIds(local.deletedReviewIds);

    const lessonUpdates = [
      ...local.uncompletedLessons.map(key => ({ key, isCompleted: false })),
      ...local.completedLessons.map(key => ({ key, isCompleted: true }))
    ];
    for (const { key, isCompleted } of lessonUpdates) {
      const split = key.lastIndexOf('::');
      if (split < 1) continue;
      await firstValueFrom(this.http.put<void>(`${environment.apiBaseUrl}/learning/lessons`, {
        moduleId: key.slice(0, split), lessonId: key.slice(split + 2), isCompleted
      }));
      lessonCount++;
    }
    if (local.uncompletedLessons.length) this.progress.clearUncompletedLessons(local.uncompletedLessons);

    for (const item of local.reviews) {
      await firstValueFrom(this.http.put<void>(`${environment.apiBaseUrl}/learning/reviews`, {
        itemKey: item.id, title: item.title, moduleId: item.moduleId, moduleTitle: item.moduleTitle,
        kind: item.kind, reason: item.reason, isResolved: item.done
      }));
      reviewCount++;
    }

    const synced = this.readSyncedAttempts();
    for (const attempt of local.quizAttempts) {
      const signature = this.attemptSignature(attempt);
      if (synced.has(signature)) continue;
      await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/learning/quiz-attempts`, {
        score: attempt.score, total: attempt.total, wrongQuestionIds: attempt.wrongQuestionIds
      }));
      synced.add(signature); quizCount++;
      this.saveSyncedAttempts(synced);
    }

    for (const [flashcardId, rating] of Object.entries(local.flashcardRatings)) {
      await firstValueFrom(this.http.put<void>(`${environment.apiBaseUrl}/learning/flashcards`, { flashcardId, rating }));
      flashcardCount++;
    }

    const remote = await firstValueFrom(this.http.get<ServerSnapshot>(`${environment.apiBaseUrl}/learning/snapshot`));
    for (const attempt of remote.quizAttempts) {
      synced.add(this.attemptSignature({ date: attempt.completedAt, score: attempt.score, total: attempt.total, wrongQuestionIds: attempt.wrongQuestionIds }));
    }
    this.saveSyncedAttempts(synced);
    const remoteState: ProgressSnapshot = {
      completedLessons: remote.lessons.filter(x => x.isCompleted).map(x => `${x.moduleId}::${x.lessonId}`),
      reviews: remote.reviews.map(x => ({ id: x.itemKey, title: x.title, moduleId: x.moduleId, moduleTitle: x.moduleTitle, kind: x.kind, reason: x.reason, done: x.isResolved, createdAt: x.createdAt })),
      quizAttempts: remote.quizAttempts.map(x => ({ date: x.completedAt, score: x.score, total: x.total, wrongQuestionIds: x.wrongQuestionIds })),
      flashcardRatings: Object.fromEntries(remote.flashcards.map(x => [x.flashcardId, x.rating])),
      deletedReviewIds: [],
      uncompletedLessons: []
    };
    this.progress.mergeSnapshot(remoteState);
    return { lessons: lessonCount, reviews: reviewCount, quizzes: quizCount, flashcards: flashcardCount, replacedLocal };
  }

  private attemptSignature(attempt: { date: string; score: number; total: number; wrongQuestionIds: string[] }): string {
    return `${attempt.date}|${attempt.score}|${attempt.total}|${[...attempt.wrongQuestionIds].sort().join(',')}`;
  }
  private readSyncedAttempts(): Set<string> {
    try { return new Set(JSON.parse(localStorage.getItem(SYNCED_ATTEMPTS_KEY) ?? '[]') as string[]); }
    catch { return new Set<string>(); }
  }
  private saveSyncedAttempts(items: Set<string>): void {
    try { localStorage.setItem(SYNCED_ATTEMPTS_KEY, JSON.stringify([...items].slice(-300))); } catch { /* Local sync marker is best-effort. */ }
  }
}
