import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth.service';
import { LearningSyncService } from '../../core/learning-sync.service';

const count = (n: number, singular: string, plural: string): string => `${n} ${n === 1 ? singular : plural}`;

@Component({
  standalone: true,
  selector: 'app-account',
  imports: [FormsModule],
  template: `
    <section class="page-heading"><span class="eyebrow">Conta e sincronização</span><h1>A tua academia, em qualquer dispositivo</h1><p>Cria uma conta para guardar o progresso na base de dados e sincronizá-lo com esta aplicação.</p></section>
    @if (auth.currentUser(); as user) {
      <section class="account-card">
        <div class="account-avatar">{{ user.displayName.slice(0, 1).toUpperCase() }}</div>
        <span class="eyebrow">Sessão iniciada</span><h2>{{ user.displayName }}</h2><p>{{ user.email }}</p>
        <div class="account-actions"><button class="button" type="button" (click)="sync()" [disabled]="busy()">{{ busy() ? 'A sincronizar…' : 'Sincronizar progresso agora' }}</button><button class="button button-outline" type="button" (click)="logout()">Terminar sessão</button></div>
        @if (message()) { <div class="notice notice-success" role="status">{{ message() }}</div> }
        @if (error()) { <div class="notice notice-error" role="alert">{{ error() }}</div> }
      </section>
      <section class="account-note"><h2>O que é sincronizado?</h2><p>Aulas concluídas, lista de revisão, resultados de quizzes e avaliações de flashcards. O conteúdo de estudo continua disponível mesmo sem iniciar sessão.</p></section>
    } @else {
      <section class="account-card account-form-card">
        @if (auth.sessionExpired() && !error()) { <div class="notice notice-error" role="status">A tua sessão expirou. Inicia sessão novamente para sincronizar o progresso.</div> }
        <div class="account-tabs"><button type="button" class="filter-chip" [class.active]="mode() === 'login'" (click)="setMode('login')">Iniciar sessão</button><button type="button" class="filter-chip" [class.active]="mode() === 'register'" (click)="setMode('register')">Criar conta</button></div>
        <form (ngSubmit)="submit()" #accountForm="ngForm">
          @if (mode() === 'register') { <label for="displayName">Nome</label><input id="displayName" name="displayName" [(ngModel)]="displayName" required minlength="2" maxlength="100" autocomplete="name" /> }
          <label for="email">Email</label><input id="email" name="email" type="email" [(ngModel)]="email" required maxlength="320" autocomplete="email" />
          <label for="password">Palavra-passe</label><input id="password" name="password" type="password" [(ngModel)]="password" required minlength="10" maxlength="128" [autocomplete]="mode() === 'login' ? 'current-password' : 'new-password'" />
          @if (mode() === 'register') { <p class="form-hint">Usa pelo menos 10 caracteres. Nunca reutilizes uma palavra-passe importante.</p> }
          <button class="button button-primary account-submit" type="submit" [disabled]="accountForm.invalid || busy()">{{ busy() ? 'A processar…' : mode() === 'login' ? 'Iniciar sessão' : 'Criar conta e sincronizar' }}</button>
        </form>
        @if (error()) { <div class="notice notice-error" role="alert">{{ error() }}</div> }
        <p class="form-hint">A API deve estar em execução em <code>http://localhost:5080</code>. Sem backend, podes continuar a estudar com os dados locais.</p>
      </section>
    }
  `
})
export class AccountComponent {
  readonly mode = signal<'login'|'register'>('login');
  readonly busy = signal(false);
  readonly message = signal('');
  readonly error = signal('');
  displayName = '';
  email = '';
  password = '';

  constructor(readonly auth: AuthService, private readonly syncService: LearningSyncService) {
    auth.checkExpiry();
  }

  setMode(mode: 'login'|'register'): void { this.mode.set(mode); this.error.set(''); this.message.set(''); }

  submit(): void {
    this.error.set(''); this.message.set(''); this.busy.set(true);
    const request = this.mode() === 'login'
      ? this.auth.login(this.email.trim(), this.password)
      : this.auth.register(this.displayName.trim(), this.email.trim(), this.password);
    request.subscribe({
      next: () => {
        this.busy.set(false);
        void this.sync().catch(() => { this.message.set('Sessão iniciada. A sincronização pode ser repetida quando a API e a base de dados estiverem disponíveis.'); this.error.set(''); });
      },
      error: (err: unknown) => { this.busy.set(false); this.error.set(this.describeError(err)); }
    });
  }

  async sync(): Promise<void> {
    this.error.set(''); this.message.set(''); this.busy.set(true);
    try {
      const result = await this.syncService.sync();
      const replaced = result.replacedLocal ? 'O progresso local pertencia a outra conta e foi substituído pelo desta. ' : '';
      this.message.set(`${replaced}Sincronização concluída. Progresso guardado na conta: ${count(result.lessons, 'aula concluída', 'aulas concluídas')}, ${count(result.reviews, 'tema por rever', 'temas por rever')}, ${count(result.quizzes, 'sessão de quiz', 'sessões de quiz')} e ${count(result.flashcards, 'flashcard avaliado', 'flashcards avaliados')}.`);
    } catch (err) {
      this.error.set(this.describeError(err));
      throw err;
    } finally { this.busy.set(false); }
  }

  logout(): void { this.auth.logout(); this.message.set(''); this.error.set(''); }

  private describeError(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 0) return 'Não foi possível contactar a API. Confirma se o backend e o SQL Server estão em execução.';
      // 401 no login é credencial errada; noutro pedido (ex.: sincronização) a sessão expirou e o interceptor já a terminou.
      if (error.status === 401) return error.url?.includes('/auth/login') ? 'Email ou palavra-passe incorretos.' : 'A tua sessão expirou. Inicia sessão novamente para sincronizar o progresso.';
      if (error.status === 409) return 'Já existe uma conta com este email. Inicia sessão.';
      if (error.error?.message) return String(error.error.message);
      // ValidationProblem: { errors: { campo: [mensagens] } }
      const fieldErrors = error.error?.errors as Record<string, string[]> | undefined;
      if (fieldErrors && typeof fieldErrors === 'object') {
        const messages = Object.values(fieldErrors).flat().filter(Boolean);
        if (messages.length) return messages.join(' ');
      }
      return `O pedido falhou (HTTP ${error.status}). Verifica os dados e tenta novamente.`;
    }
    return 'Ocorreu um erro inesperado. Tenta novamente.';
  }
}
