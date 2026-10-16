import { Component, computed, signal } from '@angular/core';
import { FLASHCARDS } from '../../data/study-data';
import { ProgressService } from '../../core/progress.service';

@Component({
  standalone: true,
  selector: 'app-flashcards',
  template: `
    <section class="page-heading">
      <span class="eyebrow">Revisão ativa</span><h1>Flashcards</h1>
      <p>Tenta responder mentalmente antes de revelar a resposta. A recuperação ativa ajuda a identificar o que já sabes.</p>
    </section>
    <div class="flashcard-toolbar">
      <span class="level">{{ currentCard().topic }}</span>
      <span class="muted">{{ currentIndex() + 1 }} de {{ cards.length }}</span>
    </div>
    <button class="flashcard" type="button" (click)="revealed.set(!revealed())" [attr.aria-label]="revealed() ? 'Ocultar resposta' : 'Revelar resposta'">
      <span class="eyebrow">{{ revealed() ? 'Resposta' : 'Pergunta' }}</span>
      <span class="flashcard-text">{{ revealed() ? currentCard().back : currentCard().front }}</span>
      <span class="flip-hint">{{ revealed() ? 'Clica para voltar à pergunta' : 'Clica para revelar a resposta' }}</span>
    </button>
    @if (revealed()) {
      <div class="flashcard-rating"><span>Como te correu?</span>
        <button class="button button-outline" type="button" (click)="rate('again')">Preciso rever</button>
        <button class="button button-outline" type="button" (click)="rate('good')">Sei explicar</button>
        <button class="button button-outline" type="button" (click)="rate('easy')">Domino</button>
      </div>
    }
    <div class="flashcard-actions">
      <button class="button button-outline" type="button" (click)="previous()" [disabled]="currentIndex() === 0">← Anterior</button>
      <button class="button" type="button" (click)="next()">{{ currentIndex() === cards.length - 1 ? 'Começar novamente' : 'Seguinte →' }}</button>
    </div>
    <div class="callout"><strong>Dica de estudo:</strong> se não conseguires explicar o conceito com as tuas palavras, marca-o mentalmente para rever e procura um exemplo de código.</div>
  `
})
export class FlashcardsComponent {
  constructor(private readonly progress: ProgressService) {}
  readonly cards = FLASHCARDS;
  readonly currentIndex = signal(0);
  readonly revealed = signal(false);
  readonly currentCard = computed(() => this.cards[this.currentIndex()]);
  rate(rating: 'again' | 'good' | 'easy'): void {
    const card = this.currentCard();
    this.progress.rateFlashcard(card.id, rating, card.front, card.moduleId, card.moduleTitle);
  }

  previous(): void {
    this.currentIndex.update(index => Math.max(0, index - 1));
    this.revealed.set(false);
  }
  next(): void {
    this.currentIndex.update(index => index === this.cards.length - 1 ? 0 : index + 1);
    this.revealed.set(false);
  }
}