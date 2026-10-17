import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  auth.checkExpiry();
  const token = auth.token();
  if (!token) return next(request);
  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: unknown) => {
      // Só um pedido que levava token pode revelar uma sessão inválida; o 401 do login é credencial errada.
      if (error instanceof HttpErrorResponse && error.status === 401) auth.expireSession();
      return throwError(() => error);
    })
  );
};
