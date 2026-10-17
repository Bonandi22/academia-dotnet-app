import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AuthUser { id: string; displayName: string; email: string; }
export interface AuthResponse { accessToken: string; expiresAt: string; user: AuthUser; }

const SESSION_KEY = 'academia-dotnet-session-v2';
// Chaves da versão anterior, que guardava token e utilizador sem a data de expiração.
const LEGACY_KEYS = ['academia-dotnet-access-token-v1', 'academia-dotnet-user-v1'];
// Margem para não enviar um token que expira durante o pedido (a API tolera 30 s de ClockSkew).
const EXPIRY_MARGIN_MS = 30_000;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly session = signal<AuthResponse | null>(this.loadSession());
  private readonly expired = signal(false);
  readonly currentUser = computed(() => this.session()?.user ?? null);
  readonly sessionExpired = this.expired.asReadonly();

  constructor(private readonly http: HttpClient) {}

  token(): string | null {
    const session = this.session();
    return session && !this.isExpired(session) ? session.accessToken : null;
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { email, password }).pipe(tap(response => this.store(response)));
  }

  register(displayName: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/register`, { displayName, email, password }).pipe(tap(response => this.store(response)));
  }

  logout(): void {
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
    try { localStorage.setItem(SESSION_KEY, JSON.stringify(response)); } catch { /* The API remains available for this session. */ }
    this.session.set(response);
    this.expired.set(false);
  }

  private clear(): void {
    try { for (const key of [SESSION_KEY, ...LEGACY_KEYS]) localStorage.removeItem(key); } catch { /* Storage may be disabled. */ }
    this.session.set(null);
  }

  private loadSession(): AuthResponse | null {
    try {
      if (typeof localStorage === 'undefined') return null;
      // Sessões antigas não têm expiresAt: descartam-se e o utilizador volta a iniciar sessão.
      for (const key of LEGACY_KEYS) localStorage.removeItem(key);
      const raw = localStorage.getItem(SESSION_KEY);
      if (!raw) return null;
      const session = JSON.parse(raw) as AuthResponse;
      if (!session?.accessToken || !session.user?.id || this.isExpired(session)) {
        localStorage.removeItem(SESSION_KEY);
        return null;
      }
      return session;
    } catch { return null; }
  }
}
