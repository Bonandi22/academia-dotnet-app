import { Injectable, computed, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface AuthUser { id: string; displayName: string; email: string; }
export interface AuthResponse { accessToken: string; expiresAt: string; user: AuthUser; }

const TOKEN_KEY = 'academia-dotnet-access-token-v1';
const USER_KEY = 'academia-dotnet-user-v1';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly currentUserState = signal<AuthUser | null>(this.loadUser());
  readonly currentUser = computed(() => this.currentUserState());
  readonly isAuthenticated = computed(() => !!this.token() && !!this.currentUserState());

  constructor(private readonly http: HttpClient) {}

  token(): string | null {
    try { return typeof localStorage === 'undefined' ? null : localStorage.getItem(TOKEN_KEY); }
    catch { return null; }
  }

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { email, password }).pipe(tap(response => this.store(response)));
  }

  register(displayName: string, email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/register`, { displayName, email, password }).pipe(tap(response => this.store(response)));
  }

  logout(): void {
    try { localStorage.removeItem(TOKEN_KEY); localStorage.removeItem(USER_KEY); } catch { /* Storage may be disabled. */ }
    this.currentUserState.set(null);
  }

  private store(response: AuthResponse): void {
    try { localStorage.setItem(TOKEN_KEY, response.accessToken); localStorage.setItem(USER_KEY, JSON.stringify(response.user)); } catch { /* The API remains available for this session. */ }
    this.currentUserState.set(response.user);
  }

  private loadUser(): AuthUser | null {
    try {
      const raw = typeof localStorage === 'undefined' ? null : localStorage.getItem(USER_KEY);
      if (!raw) return null;
      const user = JSON.parse(raw) as AuthUser;
      return user?.id && user?.email ? user : null;
    } catch { return null; }
  }
}
