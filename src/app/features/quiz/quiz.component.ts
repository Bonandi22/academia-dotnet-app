import { Component, computed, signal } from '@angular/core';
import { QUIZ_QUESTIONS } from '../../data/study-data';
import { ProgressService } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-quiz',
  template: `
    <section class="page-heading">
      <span class="eyebrow">Avalia os teus conhecimentos</span><h1>Quiz técnico</h1>
      <p>Escolhe uma resposta por pergunta. No final, recebe a pontuação e a explicação de cada resposta.</p>
    </section>
    <section class="quiz-summary">
      <div><span class="muted">Pergunta</span><strong>{{ Math.min(currentIndex() + 1, questions.length) }} <small>/ {{ questions.length }}</small></strong></div>
      <div class="progress-track"><div class="progress-fill" [style.width.%]="progress()"></div></div>
      <span class="level">{{ currentQuestion().level }}</span>
    </section>
    @if (!finished()) {
      <article class="quiz-card">
        <span class="category">{{ currentQuestion().topic }}</span>
        <h2>{{ currentQuestion().prompt }}</h2>
        <div class="answers">
          @for (option of currentQuestion().options; track option; let i = $index) {
            <button type="button" class="answer-option"
              [class.selected]="selectedAnswer() === i"
              [class.correct]="revealed() && i === currentQuestion().correctIndex"
              [class.incorrect]="revealed() && selectedAnswer() === i && i !== currentQuestion().correctIndex"
              [disabled]="revealed()"
              (click)="selectedAnswer.set(i)">
              <span class="option-letter">{{ letter(i) }}</span><span>{{ option }}</span>
              @if (revealed() && i === currentQuestion().correctIndex) { <span class="answer-mark">✓</span> }
            </button>
          }
        </div>
        @if (revealed()) {
          <div class="explanation" [class.explanation-good]="selectedAnswer() === currentQuestion().correctIndex">
            <strong>{{ selectedAnswer() === currentQuestion().correctIndex ? 'Correto!' : 'Vamos rever este conceito.' }}</strong>
            <p>{{ currentQuestion().explanation }}</p>
          </div>
        }
        <div class="quiz-actions">
          <button class="button button-outline" type="button" (click)="reset()" [disabled]="currentIndex() === 0">Anterior</button>
          @if (!revealed()) {
            <button class="button" type="button" (click)="checkAnswer()" [disabled]="selectedAnswer() === null">Confirmar resposta</button>
          } @else {
            <button class="button" type="button" (click)="next()">{{ currentIndex() === questions.length - 1 ? 'Ver resultado' : 'Próxima pergunta →' }}</button>
          }
        </div>
      </article>
    } @else {
      <article class="quiz-card final-score">
        <div class="score-icon">✓</div><span class="eyebrow">Sessão concluída</span>
        <h2>Resultado do teu quiz</h2><div class="big-score">{{ score() }} <span>/ {{ questions.length }}</span></div>
        <p>{{ scoreMessage() }}</p>
        <p class="muted">Resultado guardado no teu histórico. As respostas erradas foram adicionadas à lista de revisão.</p>
        <div class="score-breakdown"><span>Corretas <b>{{ score() }}</b></span><span>A rever <b>{{ questions.length - score() }}</b></span></div>
        <button class="button" type="button" (click)="restart()">Repetir quiz</button>
      </article>
    }
  `
})
export class QuizComponent {
  constructor(private readonly progressService: ProgressService) {}

  readonly Math = Math;
  readonly questions = QUIZ_QUESTIONS;
  readonly currentIndex = signal(0);
  readonly selectedAnswer = signal<number | null>(null);
  readonly revealed = signal(false);
  readonly finished = signal(false);
  readonly answers = signal<Record<string, number>>({});
  readonly currentQuestion = computed(() => this.questions[this.currentIndex()]);
  readonly progress = computed(() => ((this.currentIndex() + 1) / this.questions.length) * 100);
  readonly score = computed(() => this.questions.filter(q => this.answers()[q.id] === q.correctIndex).length);
  readonly scoreMessage = computed(() => {
    const ratio = this.score() / this.questions.length;
    return ratio >= 0.8 ? 'Boa base! Continua a aprofundar os trade-offs e os cenários de produção.'
      : ratio >= 0.6 ? 'Bom começo. Revê as explicações e repete o quiz para consolidar os conceitos.'
      : 'Usa este resultado como mapa de estudo. Revê os módulos recomendados e tenta novamente.';
  });

  private moduleForTopic(topic: string): { id: string; title: string } {
    const normalized = topic.toLowerCase();
    if (normalized.includes('sql') || normalized.includes('ef core') || normalized.includes('dados')) return { id: 'data', title: 'EF Core e SQL Server' };
    if (normalized.includes('microserv')) return { id: 'microservices', title: 'Microserviços e RabbitMQ' };
    if (normalized.includes('angular')) return { id: 'angular', title: 'Angular e RxJS' };
    if (normalized.includes('react')) return { id: 'react', title: 'React e microfrontends' };
    if (normalized.includes('arquitetura') || normalized.includes('clean architecture')) return { id: 'architecture', title: 'Arquitetura e padrões' };
    if (normalized.includes('asp.net') || normalized.includes('api') || normalized.includes('http')) return { id: 'aspnet', title: 'ASP.NET Core e APIs' };
    return { id: 'csharp', title: 'C# e plataforma .NET' };
  }

  letter(index: number): string { return String.fromCharCode(65 + index); }
  checkAnswer(): void {
    if (this.selectedAnswer() === null) return;
    const q = this.currentQuestion();
    this.answers.update(current => ({ ...current, [q.id]: this.selectedAnswer()! }));
    if (this.selectedAnswer() !== q.correctIndex) {
      const target = this.moduleForTopic(q.topic);
      this.progressService.addReview({
        id: `quiz:${q.id}`, title: q.prompt, moduleId: target.id, moduleTitle: target.title,
        kind: 'quiz', reason: `Resposta incorreta no quiz. Revê o conceito: ${q.explanation}`
      });
    } else {
      this.progressService.resolveReview(`quiz:${q.id}`);
    }
    this.revealed.set(true);
  }
  next(): void {
    if (this.currentIndex() === this.questions.length - 1) {
      const answers = this.answers();
      const wrongIds = this.questions.filter(q => answers[q.id] !== q.correctIndex).map(q => q.id);
      this.progressService.recordQuiz(this.score(), this.questions.length, wrongIds);
      this.finished.set(true);
      return;
    }
    this.currentIndex.update(index => index + 1);
    this.selectedAnswer.set(null);
    this.revealed.set(false);
  }
  reset(): void {
    this.currentIndex.update(index => Math.max(0, index - 1));
    this.selectedAnswer.set(null);
    this.revealed.set(false);
  }
  restart(): void {
    this.currentIndex.set(0);
    this.selectedAnswer.set(null);
    this.revealed.set(false);
    this.finished.set(false);
    this.answers.set({});
  }
}