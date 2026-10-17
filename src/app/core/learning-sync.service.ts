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

// Registo de assinaturas da versão anterior; substituído pelo id da tentativa, só falta limpá-lo.
const LEGACY_SYNCED_ATTEMPTS_KEY = 'academia-dotnet-synced-quiz-attempts-v1';

@Injectable({ providedIn: 'root' })
export class LearningSyncService {
  constructor(private readonly http: HttpClient, private readonly progress: ProgressService, private readonly auth: AuthService) {}

  async sync(): Promise<{ lessons: number; reviews: number; quizzes: number; flashcards: number; replacedLocal: boolean }> {
    const userId = this.auth.currentUser()?.id;
    // Progresso de outra conta não pode ser enviado para esta.
    const replacedLocal = userId ? this.progress.claimFor(userId) : false;
    try { localStorage.removeItem(LEGACY_SYNCED_ATTEMPTS_KEY); } catch { /* Sem storage não há nada a limpar. */ }
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

    // A API é idempotente pelo id: reenviar uma tentativa já gravada não a duplica.
    for (const attempt of local.quizAttempts) {
      await firstValueFrom(this.http.post(`${environment.apiBaseUrl}/learning/quiz-attempts`, {
        id: attempt.id, score: attempt.score, total: attempt.total, wrongQuestionIds: attempt.wrongQuestionIds, completedAt: attempt.date
      }));
      quizCount++;
    }

    for (const [flashcardId, rating] of Object.entries(local.flashcardRatings)) {
      await firstValueFrom(this.http.put<void>(`${environment.apiBaseUrl}/learning/flashcards`, { flashcardId, rating }));
      flashcardCount++;
    }

    const remote = await firstValueFrom(this.http.get<ServerSnapshot>(`${environment.apiBaseUrl}/learning/snapshot`));
    const remoteState: ProgressSnapshot = {
      completedLessons: remote.lessons.filter(x => x.isCompleted).map(x => `${x.moduleId}::${x.lessonId}`),
      reviews: remote.reviews.map(x => ({ id: x.itemKey, title: x.title, moduleId: x.moduleId, moduleTitle: x.moduleTitle, kind: x.kind, reason: x.reason, done: x.isResolved, createdAt: x.createdAt })),
      quizAttempts: remote.quizAttempts.map(x => ({ id: x.id, date: x.completedAt, score: x.score, total: x.total, wrongQuestionIds: x.wrongQuestionIds })),
      flashcardRatings: Object.fromEntries(remote.flashcards.map(x => [x.flashcardId, x.rating])),
      deletedReviewIds: [],
      uncompletedLessons: []
    };
    this.progress.mergeSnapshot(remoteState);
    return { lessons: lessonCount, reviews: reviewCount, quizzes: quizCount, flashcards: flashcardCount, replacedLocal };
  }
}
