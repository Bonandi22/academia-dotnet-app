import { Component, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { STUDY_LESSONS, StudyLesson } from '../../data/study-content';
import { ProgressService } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-study',
  imports: [RouterLink],
  template: `
    @if (lesson(); as current) {
      <a class="back-link" routerLink="/modulos">← Voltar aos módulos</a>
      <section class="lesson-heading">
        <span class="eyebrow">Academia .NET · Guia de estudo</span>
        <h1>{{ current.title }}</h1>
        <p>{{ current.subtitle }}</p>
        <div class="lesson-meta"><span>◷ {{ current.estimatedMinutes }} min</span><span>{{ current.sections.length }} aulas</span><span>{{ current.interviewQuestions.length }} perguntas de entrevista</span></div>
        <div class="progress-panel"><div class="progress-panel-label"><strong>Progresso deste módulo</strong><span>{{ progress.lessonProgress(current.moduleId, current.sections.length) }}%</span></div><div class="progress-track"><div class="progress-fill" [style.width.%]="progress.lessonProgress(current.moduleId, current.sections.length)"></div></div></div>
      </section>

      <section class="lesson-objectives">
        <h2>O que vais aprender</h2>
        <ul>@for (objective of current.objectives; track objective) { <li>{{ objective }}</li> }</ul>
      </section>

      <div class="lesson-layout">
        <main class="lesson-content">
          @for (section of current.sections; track section.title; let i = $index) {
            <article class="lesson-section">
              <div class="lesson-section-top"><span class="lesson-section-number">AULA {{ i + 1 }}</span><span class="lesson-status" [class.is-complete]="progress.isLessonComplete(current.moduleId, i)">{{ progress.isLessonComplete(current.moduleId, i) ? 'Concluída' : 'Por estudar' }}</span></div>
              <h2>{{ section.title }}</h2>
              <p>{{ section.explanation }}</p>
              @if (section.code) {
                <div class="code-example"><div class="code-example-top"><span>Exemplo</span><button type="button" class="copy-code" (click)="copyCode(section.code, section.title)">{{ copied() === section.title ? 'Copiado' : 'Selecionar código' }}</button></div><pre><code>{{ section.code }}</code></pre></div>
              }
              @if (section.takeaway) { <div class="lesson-takeaway"><strong>Para recordar</strong><p>{{ section.takeaway }}</p></div> }
              <div class="lesson-actions"><button type="button" class="button" [class.button-outline]="progress.isLessonComplete(current.moduleId, i)" (click)="toggleComplete(current, i)">{{ progress.isLessonComplete(current.moduleId, i) ? 'Marcar como não concluída' : 'Marcar aula como concluída' }}</button><button type="button" class="text-button" (click)="toggleReview(current, i, section.title)">{{ isReviewPending(current.moduleId, i) ? '✓ Na lista de revisão' : '+ Preciso rever esta aula' }}</button></div>
            </article>
          }
        </main>
        <aside class="lesson-sidebar">
          <div class="sidebar-card"><h2>Responde na entrevista</h2>
            @for (item of current.interviewQuestions; track item.question; let i = $index) {
              <details class="interview-item"><summary>{{ i + 1 }}. {{ item.question }}</summary><p>{{ item.answer }}</p></details>
            }
          </div>
          <div class="sidebar-card exercise-card"><span class="eyebrow">Desafio prático</span><h2>Resolve o cenário</h2><p>{{ current.exercise.scenario }}</p><h3>O teu objetivo</h3><p>{{ current.exercise.task }}</p>
            <button type="button" class="button button-outline" (click)="showSolution.set(!showSolution())">{{ showSolution() ? 'Ocultar solução' : 'Ver solução comentada' }}</button>
            @if (showSolution()) { <div class="solution"><strong>Uma resposta possível</strong><p>{{ current.exercise.solution }}</p></div> }
          </div>
          <a class="button button-primary lesson-next" routerLink="/quiz">Praticar no quiz →</a>
        </aside>
      </div>
    } @else {
      <section class="empty-state"><h1>Conteúdo ainda não disponível</h1><p>Este módulo ainda não tem um guia de estudo.</p><a class="button" routerLink="/modulos">Voltar aos módulos</a></section>
    }
  `
})
export class StudyComponent {
  readonly lesson = signal<StudyLesson | null>(null);
  readonly showSolution = signal(false);
  readonly copied = signal<string | null>(null);

  constructor(private readonly route: ActivatedRoute, readonly progress: ProgressService) {
    this.route.paramMap.subscribe(params => {
      const moduleId = params.get('moduleId');
      this.lesson.set(STUDY_LESSONS.find(item => item.moduleId === moduleId) ?? null);
      this.showSolution.set(false);
      this.copied.set(null);
    });
  }

  toggleComplete(lesson: StudyLesson, index: number): void {
    this.progress.toggleLesson(lesson.moduleId, index);
  }

  isReviewPending(moduleId: string, index: number): boolean {
    return this.progress.pendingReviews().some(item => item.id === `lesson:${moduleId}:${index}`);
  }

  toggleReview(lesson: StudyLesson, index: number, title: string): void {
    const id = `lesson:${lesson.moduleId}:${index}`;
    if (this.isReviewPending(lesson.moduleId, index)) {
      this.progress.resolveReview(id);
      return;
    }
    this.progress.addReview({
      id, title, moduleId: lesson.moduleId, moduleTitle: lesson.title,
      kind: 'lesson', reason: 'Assinalaste esta aula para revisão manual.'
    });
  }

  async copyCode(code: string, title: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(code);
      this.copied.set(title);
    } catch {
      this.copied.set('');
    }
  }
}
