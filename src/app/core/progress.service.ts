import { Injectable, computed, signal } from '@angular/core';
import { migrateLessonKey, migrateReviewId } from './legacy-lesson-keys';

export interface ReviewItem {
  id: string;
  title: string;
  moduleId: string;
  moduleTitle: string;
  kind: 'lesson' | 'quiz' | 'flashcard';
  reason: string;
  createdAt: string;
  done: boolean;
}

export interface QuizAttempt {
  date: string;
  score: number;
  total: number;
  wrongQuestionIds: string[];
}

export interface ProgressSnapshot {
  completedLessons: string[];
  reviews: ReviewItem[];
  quizAttempts: QuizAttempt[];
  flashcardRatings: Record<string, 'again' | 'good' | 'easy'>;
  deletedReviewIds: string[];
  /** Aulas desmarcadas ainda não enviadas; sem isto o pull volta a marcá-las como concluídas. */
  uncompletedLessons: string[];
}

const STORAGE_KEY = 'academia-dotnet-progress-v1';
// Id do utilizador cuja conta absorveu o progresso local; ausente enquanto o progresso é anónimo.
const OWNER_KEY = 'academia-dotnet-progress-owner-v1';
const EMPTY_STATE: ProgressSnapshot = {
  completedLessons: [],
  reviews: [],
  quizAttempts: [],
  flashcardRatings: {},
  deletedReviewIds: [],
  uncompletedLessons: []
};

@Injectable({ providedIn: 'root' })
export class ProgressService {
  private readonly state = signal<ProgressSnapshot>(this.load());
  readonly completedLessons = computed(() => this.state().completedLessons);
  readonly reviews = computed(() => this.state().reviews);
  readonly pendingReviews = computed(() => this.state().reviews.filter(item => !item.done));
  readonly quizAttempts = computed(() => this.state().quizAttempts);
  readonly flashcardRatings = computed(() => this.state().flashcardRatings);
  readonly latestQuiz = computed(() => this.state().quizAttempts[0] ?? null);
  readonly completedLessonCount = computed(() => this.state().completedLessons.length);
  readonly pendingReviewCount = computed(() => this.pendingReviews().length);

  isLessonComplete(moduleId: string, sectionId: string): boolean {
    return this.state().completedLessons.includes(this.lessonKey(moduleId, sectionId));
  }

  toggleLesson(moduleId: string, sectionId: string): void {
    const key = this.lessonKey(moduleId, sectionId);
    this.update(state => {
      const wasComplete = state.completedLessons.includes(key);
      return {
        ...state,
        completedLessons: wasComplete ? state.completedLessons.filter(item => item !== key) : [...state.completedLessons, key],
        uncompletedLessons: wasComplete
          ? [...new Set([...state.uncompletedLessons, key])]
          : state.uncompletedLessons.filter(item => item !== key)
      };
    });
  }

  lessonProgress(moduleId: string, totalSections: number): number {
    if (!totalSections) return 0;
    const prefix = `${moduleId}::`;
    const completed = this.state().completedLessons.filter(key => key.startsWith(prefix)).length;
    return Math.min(100, Math.round(completed / totalSections * 100));
  }

  addReview(item: Omit<ReviewItem, 'createdAt' | 'done'>): void {
    this.update(state => {
      const existing = state.reviews.find(review => review.id === item.id);
      const next: ReviewItem = { ...item, createdAt: new Date().toISOString(), done: false };
      return {
        ...state,
        deletedReviewIds: state.deletedReviewIds.filter(id => id !== item.id),
        reviews: existing
          ? state.reviews.map(review => review.id === item.id ? { ...review, ...next } : review)
          : [next, ...state.reviews]
      };
    });
  }

  resolveReview(id: string): void {
    this.update(state => ({
      ...state,
      reviews: state.reviews.map(item => item.id === id ? { ...item, done: true } : item)
    }));
  }

  reopenReview(id: string): void {
    this.update(state => ({
      ...state,
      reviews: state.reviews.map(item => item.id === id ? { ...item, done: false } : item)
    }));
  }

  recordQuiz(score: number, total: number, wrongQuestionIds: string[]): void {
    this.update(state => ({
      ...state,
      quizAttempts: [{ date: new Date().toISOString(), score, total, wrongQuestionIds }, ...state.quizAttempts].slice(0, 30)
    }));
  }

  rateFlashcard(id: string, rating: 'again' | 'good' | 'easy', title: string, moduleId: string, moduleTitle: string): void {
    this.update(state => ({
      ...state,
      flashcardRatings: { ...state.flashcardRatings, [id]: rating }
    }));
    if (rating === 'again') {
      this.addReview({ id: `flashcard:${id}`, title, moduleId, moduleTitle, kind: 'flashcard', reason: 'Marcado para rever no estudo de flashcards.' });
    } else {
      this.resolveReview(`flashcard:${id}`);
    }
  }

  clearCompletedReviews(): void {
    this.update(state => ({
      ...state,
      deletedReviewIds: [...new Set([...state.deletedReviewIds, ...state.reviews.filter(item => item.done).map(item => item.id)])],
      reviews: state.reviews.filter(item => !item.done)
    }));
  }

  /** Remove só os tombstones enviados, para não perder os criados durante a sincronização. */
  clearDeletedReviewIds(sent: string[]): void {
    const synced = new Set(sent);
    this.update(state => ({ ...state, deletedReviewIds: state.deletedReviewIds.filter(id => !synced.has(id)) }));
  }

  clearUncompletedLessons(sent: string[]): void {
    const synced = new Set(sent);
    this.update(state => ({ ...state, uncompletedLessons: state.uncompletedLessons.filter(key => !synced.has(key)) }));
  }

  /**
   * Associa o progresso local a uma conta. Se pertencia a outra conta, é descartado
   * para não ser enviado para a conta errada; devolve true nesse caso.
   */
  claimFor(userId: string): boolean {
    const owner = this.readOwner();
    const replaced = owner !== null && owner !== userId;
    if (replaced) this.update(() => structuredClone(EMPTY_STATE));
    try { localStorage.setItem(OWNER_KEY, userId); } catch { /* Sem storage não há progresso persistido a proteger. */ }
    return replaced;
  }

  exportSnapshot(): ProgressSnapshot {
    return JSON.parse(JSON.stringify(this.state())) as ProgressSnapshot;
  }

  mergeSnapshot(remote: ProgressSnapshot): void {
    const current = this.state();
    const reviewsById = new Map<string, ReviewItem>();
    const deletedIds = new Set([...(remote.deletedReviewIds ?? []), ...current.deletedReviewIds]);
    for (const item of [...(remote.reviews ?? []).filter(item => !deletedIds.has(item.id)), ...current.reviews]) {
      if (deletedIds.has(item.id)) continue;
      const existing = reviewsById.get(item.id);
      if (!existing || new Date(item.createdAt).getTime() >= new Date(existing.createdAt).getTime()) reviewsById.set(item.id, item);
    }
    const attempts: QuizAttempt[] = [...(remote.quizAttempts ?? [])];
    for (const item of current.quizAttempts) {
      const wrongKey = [...item.wrongQuestionIds].sort().join(',');
      const duplicateIndex = attempts.findIndex(existing =>
        existing.score === item.score && existing.total === item.total &&
        [...existing.wrongQuestionIds].sort().join(',') === wrongKey &&
        Math.abs(new Date(existing.date).getTime() - new Date(item.date).getTime()) < 120_000
      );
      if (duplicateIndex >= 0) attempts[duplicateIndex] = item;
      else attempts.push(item);
    }
    const uncompleted = new Set(current.uncompletedLessons);
    const next: ProgressSnapshot = {
      completedLessons: [...new Set([...(remote.completedLessons ?? []), ...current.completedLessons])].filter(key => !uncompleted.has(key)),
      reviews: [...reviewsById.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt)),
      quizAttempts: attempts.sort((a, b) => b.date.localeCompare(a.date)).slice(0, 50),
      flashcardRatings: { ...(remote.flashcardRatings ?? {}), ...current.flashcardRatings },
      deletedReviewIds: [...deletedIds],
      uncompletedLessons: [...uncompleted]
    };
    this.state.set(next);
    this.save(next);
  }

  private lessonKey(moduleId: string, sectionId: string): string {
    return `${moduleId}::${sectionId}`;
  }

  private update(updater: (state: ProgressSnapshot) => ProgressSnapshot): void {
    const next = updater(this.state());
    this.state.set(next);
    this.save(next);
  }

  private load(): ProgressSnapshot {
    try {
      if (typeof localStorage === 'undefined') return { ...EMPTY_STATE };
      const raw = localStorage.getItem(STORAGE_KEY);
      if (!raw) return { ...EMPTY_STATE };
      const parsed = JSON.parse(raw) as Partial<ProgressSnapshot>;
      // Chaves por índice (`csharp::0`, `lesson:csharp:0`) passam a id de secção; é idempotente.
      const lessonKeys = (keys: unknown) => Array.isArray(keys) ? [...new Set(keys.map(migrateLessonKey))] : [];
      return {
        completedLessons: lessonKeys(parsed.completedLessons),
        reviews: Array.isArray(parsed.reviews) ? parsed.reviews.map(item => ({ ...item, id: migrateReviewId(item.id) })) : [],
        quizAttempts: Array.isArray(parsed.quizAttempts) ? parsed.quizAttempts : [],
        flashcardRatings: parsed.flashcardRatings && typeof parsed.flashcardRatings === 'object' ? parsed.flashcardRatings : {},
        deletedReviewIds: Array.isArray(parsed.deletedReviewIds) ? parsed.deletedReviewIds.map(migrateReviewId) : [],
        uncompletedLessons: lessonKeys(parsed.uncompletedLessons)
      };
    } catch {
      return { ...EMPTY_STATE };
    }
  }

  private readOwner(): string | null {
    try { return typeof localStorage === 'undefined' ? null : localStorage.getItem(OWNER_KEY); }
    catch { return null; }
  }

  private save(state: ProgressSnapshot): void {
    try {
      if (typeof localStorage !== 'undefined') localStorage.setItem(STORAGE_KEY, JSON.stringify(state));
    } catch {
      // A aplicação continua funcional mesmo se o armazenamento do browser estiver indisponível.
    }
  }
}
