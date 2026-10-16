import { Component, computed, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { STUDY_MODULES } from '../../data/study-data';
import { STUDY_LESSONS } from '../../data/study-content';
import { ProgressService, ReviewItem } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-review',
  imports: [RouterLink],
  template: `
    <section class="page-heading">
      <span class="eyebrow">Aprendizagem adaptativa</span>
      <h1>Progresso e revisão</h1>
      <p>Acompanha o que já estudaste e concentra-te nos temas que ainda precisas de consolidar. Os dados ficam guardados neste navegador.</p>
    </section>

    <section class="progress-stats">
      <article class="progress-stat"><span>Aulas concluídas</span><strong>{{ progress.completedLessonCount() }} <small>/ {{ totalLessons }}</small></strong><div class="progress-track"><div class="progress-fill" [style.width.%]="overallProgress()"></div></div><small>{{ overallProgress() }}% do conteúdo estudado</small></article>
      <article class="progress-stat"><span>Temas a rever</span><strong>{{ progress.pendingReviewCount() }}</strong><small>Erros do quiz e itens assinalados</small></article>
      <article class="progress-stat"><span>Sessões de quiz</span><strong>{{ progress.quizAttempts().length }}</strong><small>{{ latestQuizLabel() }}</small></article>
    </section>

    <section class="section review-section">
      <div class="section-heading"><div><span class="eyebrow">Plano personalizado</span><h2>Lista de revisão</h2></div>
        <div class="review-filters"><button type="button" class="filter-chip" [class.active]="filter() === 'pending'" (click)="filter.set('pending')">Por rever</button><button type="button" class="filter-chip" [class.active]="filter() === 'all'" (click)="filter.set('all')">Todos</button></div>
      </div>
      <div class="review-list">
        @for (item of visibleReviews(); track item.id) {
          <article class="review-item" [class.review-done]="item.done">
            <div class="review-item-copy"><div class="review-item-meta"><span class="category">{{ kindLabel(item.kind) }}</span><span class="muted">{{ item.moduleTitle }}</span></div><h3>{{ item.title }}</h3><p>{{ item.reason }}</p></div>
            <div class="review-item-actions">
              @if (!item.done) { <button type="button" class="button button-outline" (click)="markDone(item)">Marcar revista</button> }
              @else { <button type="button" class="button button-outline" (click)="reopen(item)">Reabrir</button> }
              <a class="text-link" [routerLink]="['/estudo', item.moduleId]">Estudar módulo →</a>
            </div>
          </article>
        } @empty {
          <div class="empty-state"><h2>{{ filter() === 'pending' ? 'Tudo em dia!' : 'Ainda não há itens de revisão' }}</h2><p>{{ filter() === 'pending' ? 'Continua a estudar e os temas que precisares de rever aparecerão aqui.' : 'Marca uma aula para rever ou responde a um quiz para criares o teu plano personalizado.' }}</p><a class="button" routerLink="/modulos">Explorar módulos</a></div>
        }
      </div>
      @if (hasCompletedReviews()) { <button type="button" class="text-button clear-reviews" (click)="clearDone()">Limpar itens concluídos</button> }
    </section>

    <section class="section quiz-history-section">
      <div class="section-heading"><div><span class="eyebrow">Evolução</span><h2>Histórico recente de quizzes</h2></div><a routerLink="/quiz" class="text-link">Fazer novo quiz →</a></div>
      <div class="quiz-history-list">
        @for (attempt of progress.quizAttempts().slice(0, 5); track attempt.date) {
          <article class="quiz-history-item"><div><strong>{{ attempt.score }} / {{ attempt.total }}</strong><span>{{ formatDate(attempt.date) }}</span></div><div class="history-score-track"><div class="progress-fill" [style.width.%]="attempt.total ? attempt.score / attempt.total * 100 : 0"></div></div><span class="history-percent">{{ attempt.total ? Math.round(attempt.score / attempt.total * 100) : 0 }}%</span></article>
        } @empty {
          <div class="empty-state"><p>Ainda não há sessões concluídas. Faz um quiz para começares a acompanhar os teus resultados.</p><a class="button" routerLink="/quiz">Começar quiz</a></div>
        }
      </div>
    </section>

    <section class="section">
      <div class="section-heading"><div><span class="eyebrow">Visão por tecnologia</span><h2>Progresso por módulo</h2></div></div>
      <div class="module-grid">
        @for (module of modules; track module.id) {
          <article class="module-card progress-module-card"><div class="module-top"><span class="module-icon">{{ module.icon }}</span><span class="level">{{ module.level }}</span></div><h3>{{ module.title }}</h3><div class="progress-panel-label"><span>Aulas concluídas</span><strong>{{ moduleProgress(module.id) }}%</strong></div><div class="progress-track"><div class="progress-fill" [style.width.%]="moduleProgress(module.id)"></div></div><a class="text-link" [routerLink]="['/estudo', module.id]">Continuar estudo →</a></article>
        }
      </div>
    </section>
  `
})
export class ReviewComponent {
  readonly modules = STUDY_MODULES;
  readonly Math = Math;
  readonly totalLessons = STUDY_LESSONS.reduce((sum, lesson) => sum + lesson.sections.length, 0);
  readonly filter = signal<'pending' | 'all'>('pending');
  readonly visibleReviews = computed(() => {
    const items = this.progress.reviews();
    return this.filter() === 'pending' ? items.filter(item => !item.done) : items;
  });
  readonly overallProgress = computed(() => this.totalLessons ? Math.round(this.progress.completedLessonCount() / this.totalLessons * 100) : 0);

  constructor(readonly progress: ProgressService) {}

  moduleProgress(moduleId: string): number {
    const lesson = STUDY_LESSONS.find(item => item.moduleId === moduleId);
    return lesson ? this.progress.lessonProgress(moduleId, lesson.sections.length) : 0;
  }

  formatDate(value: string): string {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'Data indisponível' : new Intl.DateTimeFormat('pt-PT', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  }

  latestQuizLabel(): string {
    const attempt = this.progress.latestQuiz();
    return attempt ? `${attempt.score}/${attempt.total} na última sessão` : 'Ainda sem resultados';
  }

  kindLabel(kind: ReviewItem['kind']): string {
    return kind === 'quiz' ? 'Resposta de quiz' : kind === 'flashcard' ? 'Flashcard' : 'Aula';
  }

  hasCompletedReviews(): boolean { return this.progress.reviews().some(item => item.done); }

  markDone(item: ReviewItem): void { this.progress.resolveReview(item.id); }
  reopen(item: ReviewItem): void { this.progress.reopenReview(item.id); }
  clearDone(): void { this.progress.clearCompletedReviews(); }
}
