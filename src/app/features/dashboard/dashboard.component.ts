import { Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { STUDY_MODULES } from '../../data/study-data';
import { STUDY_LESSONS } from '../../data/study-content';
import { ProgressService } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-dashboard',
  imports: [RouterLink],
  template: `
    <section class="hero">
      <div class="hero-copy">
        <span class="eyebrow light">O teu espaço de preparação técnica</span>
        <h1>Prepara-te para a próxima<br class="desktop-break"> entrevista com confiança.</h1>
        <p>Revê os fundamentos, pratica perguntas e aprende a explicar as tuas decisões técnicas.</p>
        <div class="hero-actions">
          <a class="button button-light" routerLink="/quiz">Começar quiz <span aria-hidden="true">→</span></a>
          <a class="button button-ghost" routerLink="/modulos">Explorar módulos</a>
        </div>
      </div>
      <div class="hero-art" aria-hidden="true">
        <div class="orbit orbit-one"></div><div class="orbit orbit-two"></div>
        <div class="code-window"><div class="window-dots"><i></i><i></i><i></i></div>
          <span class="code-purple">public async</span> <span class="code-blue">Task</span><br>
          <span class="code-yellow">PrepareAsync</span>()<br>
          <span class="indent">await <span class="code-green">Practice</span>();</span><br>
          <span class="indent">return <span class="code-green">Confidence</span>;</span>
        </div>
      </div>
    </section>
    <section class="section dashboard-progress">
      <div class="section-heading"><div><span class="eyebrow">Retoma de onde ficaste</span><h2>O teu progresso</h2></div><a routerLink="/revisao" class="text-link">Ver plano de revisão →</a></div>
      <div class="dashboard-progress-grid">
        <article class="progress-stat"><span>Conteúdo concluído</span><strong>{{ overallProgress }}%</strong><div class="progress-track"><div class="progress-fill" [style.width.%]="overallProgress"></div></div><small>{{ progress.completedLessonCount() }} de {{ totalLessons }} aulas concluídas</small></article>
        <article class="progress-stat"><span>Temas a rever</span><strong>{{ progress.pendingReviewCount() }}</strong><small>Temas assinalados e respostas erradas</small><a routerLink="/revisao" class="text-link">Rever agora →</a></article>
        <article class="progress-stat"><span>Último quiz</span><strong>{{ latestQuizLabel }}</strong><small>{{ progress.quizAttempts().length }} sessões guardadas</small><a routerLink="/quiz" class="text-link">Praticar novamente →</a></article>
      </div>
    </section>
    <section class="section">
      <div class="section-heading">
        <div><span class="eyebrow">O teu percurso</span><h2>O que vamos dominar</h2></div>
        <a routerLink="/modulos" class="text-link">Ver todos os módulos →</a>
      </div>
      <div class="module-grid">
        @for (module of modules.slice(0, 4); track module.id) {
          <a class="module-card" [routerLink]="['/estudo', module.id]">
            <div class="module-top"><span class="module-icon">{{ module.icon }}</span><span class="level">{{ module.level }}</span></div>
            <h3>{{ module.title }}</h3><p>{{ module.description }}</p>
            <span class="card-link">Começar a estudar <b>↗</b></span>
          </a>
        }
      </div>
    </section>
    <section class="practice-banner">
      <div><span class="eyebrow light">Prática deliberada</span><h2>Não basta saber a resposta.<br>É preciso saber explicar o porquê.</h2>
        <p>Treina com perguntas, exemplos e cenários semelhantes aos do dia a dia de uma equipa de desenvolvimento.</p></div>
      <a routerLink="/flashcards" class="button button-light">Rever flashcards →</a>
    </section>
  `
})
export class DashboardComponent {
  readonly modules = STUDY_MODULES;
  readonly totalLessons = STUDY_LESSONS.reduce((sum, lesson) => sum + lesson.sections.length, 0);
  constructor(readonly progress: ProgressService) {}
  get overallProgress(): number {
    return this.totalLessons ? Math.round(this.progress.completedLessonCount() / this.totalLessons * 100) : 0;
  }
  get latestQuizLabel(): string {
    const attempt = this.progress.latestQuiz();
    return attempt ? `${attempt.score}/${attempt.total}` : '—';
  }
}