import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.url.startsWith(environment.apiBaseUrl)) return next(request);
  const auth = inject(AuthService);
  auth.checkExpiry();
  const hadSession = !!auth.currentUser();
  // withCredentials: o browser envia o cookie de sessão (HttpOnly) também quando a API está noutra porta (dev).
  // X-Requested-With: a API recusa com 403 pedidos que alteram dados sem ele (proteção CSRF).
  return next(request.clone({ withCredentials: true, setHeaders: { 'X-Requested-With': 'XMLHttpRequest' } })).pipe(
    catchError((error: unknown) => {
      // 401 com sessão aberta = cookie expirado ou revogado; o 401 do login sem sessão é só credencial errada.
      if (hadSession && error instanceof HttpErrorResponse && error.status === 401) auth.expireSession();
      return throwError(() => error);
    })
  );
};
