import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AuthUser { id: string; displayName: string; email: string; }
/** O token não vem no corpo: a API põe-no num cookie HttpOnly que o JavaScript não consegue ler. */
export interface AuthResponse { expiresAt: string; user: AuthUser; }

const SESSION_KEY = 'academia-dotnet-session-v3';
// Versões anteriores guardavam o próprio token no localStorage; apagam-se no arranque.
const LEGACY_KEYS = ['academia-dotnet-session-v2', 'academia-dotnet-access-token-v1', 'academia-dotnet-user-v1'];
// Margem para não contar com uma sessão que expira durante o pedido (a API tolera 30 s de ClockSkew).
const EXPIRY_MARGIN_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class AuthService {
  // Só dados para a UI (quem está ligado e até quando); a prova de sessão é o cookie, que só o servidor vê.
  private readonly session = signal<AuthResponse | null>(this.loadSession());
  private readonly expired = signal(false);
  readonly currentUser = computed(() => this.session()?.user ?? null);
  readonly sessionExpired = this.expired.asReadonly();

  constructor(private readonly http: HttpClient) {}

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { email, password }).pipe(tap(response => this.store(response)));
  }

  register(displayName: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/register`, { displayName, email, password }).pipe(tap(response => this.store(response)));
  }

  logout(): void {
    // O cookie é HttpOnly: só a API o apaga. Se a API não responder, ele expira sozinho em expiresAt.
    void firstValueFrom(this.http.post<void>(`${environment.apiBaseUrl}/auth/logout`, {})).catch(() => undefined);
    this.clear();
    this.expired.set(false);
  }

  /** Termina a sessão por expiração (data local ou 401 da API), para a UI pedir novo login. */
  expireSession(): void {
    if (!this.session()) return;
    this.clear();
    this.expired.set(true);
  }

  /** O signal não reage à passagem do tempo; quem precisa de um estado fresco chama isto. */
  checkExpiry(): void {
    const session = this.session();
    if (session && this.isExpired(session)) this.expireSession();
  }

  private isExpired(session: AuthResponse): boolean {
    const expiresAt = Date.parse(session.expiresAt);
    return Number.isNaN(expiresAt) || expiresAt - EXPIRY_MARGIN_MS <= Date.now();
  }

  private store(response: AuthResponse): void {
    const session: AuthResponse = { expiresAt: response.expiresAt, user: response.user };
    try { localStorage.setItem(SESSION_KEY, JSON.stringify(session)); } catch { /* A sessão continua válida enquanto a página estiver aberta. */ }
    this.session.set(session);
    this.expired.set(false);
  }

  private clear(): void {
    try { for (const key of [SESSION_KEY, ...LEGACY_KEYS]) localStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
    this.session.set(null);
  }

  private loadSession(): AuthResponse | null {
    try {
      if (typeof localStorage === 'undefined') return null;
      // Sessões de versões anteriores (com o token guardado aqui) descartam-se: volta-se a iniciar sessão uma vez.
      for (const key of LEGACY_KEYS) localStorage.removeItem(key);
      const raw = localStorage.getItem(SESSION_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as AuthResponse;
      if (!session?.user?.id || this.isExpired(session)) {
        localStorage.removeItem(SESSION_KEY);
        return null;
      }
      return session;
    } catch { return null; }
  }
}
