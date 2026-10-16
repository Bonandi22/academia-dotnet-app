import { Component, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <header class="topbar">
      <a class="brand" routerLink="/" aria-label="Academia .NET - início">
        <span class="brand-mark">A</span>
        <span>Academia <b>.NET</b></span>
      </a>
      <button class="menu-toggle" type="button" (click)="menuOpen.set(!menuOpen())"
        [attr.aria-expanded]="menuOpen()" aria-label="Abrir menu">☰</button>
      <nav [class.open]="menuOpen()" aria-label="Navegação principal">
        <a routerLink="/" routerLinkActive="active" [routerLinkActiveOptions]="{exact:true}"
          (click)="menuOpen.set(false)">Visão geral</a>
        <a routerLink="/modulos" routerLinkActive="active" (click)="menuOpen.set(false)">Módulos</a>
        <a routerLink="/quiz" routerLinkActive="active" (click)="menuOpen.set(false)">Quiz técnico</a>
        <a routerLink="/flashcards" routerLinkActive="active" (click)="menuOpen.set(false)">Flashcards</a>
        <a routerLink="/revisao" routerLinkActive="active" (click)="menuOpen.set(false)">Progresso</a>
        <a routerLink="/conta" routerLinkActive="active" (click)="menuOpen.set(false)">Conta</a>
      </nav>
    </header>
    <main class="app-main"><router-outlet /></main>
    <footer class="footer">
      <span>Academia .NET</span>
      <span>Aprender · Praticar · Evoluir</span>
    </footer>
  `
})
export class AppComponent {
  readonly menuOpen = signal(false);
}