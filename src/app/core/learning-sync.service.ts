import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
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

export interface SyncResult { lessons: number; reviews: number; quizzes: number; flashcards: number; replacedLocal: boolean; }

@Injectable({ providedIn: 'root' })
export class LearningSyncService {
  constructor(private readonly http: HttpClient, private readonly progress: ProgressService, private readonly auth: AuthService) {}

  /** Envia todo o progresso local num só pedido (a API aplica-o numa transação) e une a resposta ao estado local. */
  async sync(): Promise<SyncResult> {
    const userId = this.auth.currentUser()?.id;
    // Progresso de outra conta não pode ser enviado para esta.
    const replacedLocal = userId ? this.progress.claimFor(userId) : false;
    try { localStorage.removeItem(LEGACY_SYNCED_ATTEMPTS_KEY); } catch { /* Sem storage não há nada a limpar. */ }
    const local = this.progress.exportSnapshot();

    const lessons = [
      ...local.uncompletedLessons.map(key => ({ key, isCompleted: false })),
      ...local.completedLessons.map(key => ({ key, isCompleted: true }))
    ].flatMap(({ key, isCompleted }) => {
      const split = key.lastIndexOf('::');
      return split < 1 ? [] : [{ moduleId: key.slice(0, split), lessonId: key.slice(split + 2), isCompleted }];
    });

    const remote = await firstValueFrom(this.http.post<ServerSnapshot>(`${environment.apiBaseUrl}/learning/sync`, {
      deletedReviewKeys: local.deletedReviewIds,
      lessons,
      reviews: local.reviews.map(item => ({
        itemKey: item.id, title: item.title, moduleId: item.moduleId, moduleTitle: item.moduleTitle,
        kind: item.kind, reason: item.reason, isResolved: item.done
      })),
      // A API é idempotente pelo id: reenviar tentativas já gravadas não as duplica.
      quizAttempts: local.quizAttempts.map(attempt => ({
        id: attempt.id, score: attempt.score, total: attempt.total, wrongQuestionIds: attempt.wrongQuestionIds, completedAt: attempt.date
      })),
      flashcards: Object.entries(local.flashcardRatings).map(([flashcardId, rating]) => ({ flashcardId, rating }))
    }));

    // Só depois de o servidor aceitar tudo: limpa os tombstones enviados (não os criados entretanto).
    this.progress.clearDeletedReviewIds(local.deletedReviewIds);
    this.progress.clearUncompletedLessons(local.uncompletedLessons);
    const remoteState: ProgressSnapshot = {
      completedLessons: remote.lessons.filter(x => x.isCompleted).map(x => `${x.moduleId}::${x.lessonId}`),
      reviews: remote.reviews.map(x => ({ id: x.itemKey, title: x.title, moduleId: x.moduleId, moduleTitle: x.moduleTitle, kind: x.kind, reason: x.reason, done: x.isResolved, createdAt: x.createdAt })),
      quizAttempts: remote.quizAttempts.map(x => ({ id: x.id, date: x.completedAt, score: x.score, total: x.total, wrongQuestionIds: x.wrongQuestionIds })),
      flashcardRatings: Object.fromEntries(remote.flashcards.map(x => [x.flashcardId, x.rating])),
      deletedReviewIds: [],
      uncompletedLessons: []
    };
    this.progress.mergeSnapshot(remoteState);

    const merged = this.progress.exportSnapshot();
    return {
      lessons: merged.completedLessons.length, reviews: merged.reviews.filter(item => !item.done).length,
      quizzes: merged.quizAttempts.length, flashcards: Object.keys(merged.flashcardRatings).length, replacedLocal
    };
  }
}
