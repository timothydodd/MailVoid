import {
  HttpErrorResponse,
  HttpEvent,
  HttpHandler,
  HttpInterceptor,
  HttpRequest,
} from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, Observable, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth-service';

@Injectable()
export class JwtInterceptor implements HttpInterceptor {
  private authService = inject(AuthService);
  private router = inject(Router);

  intercept(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Skip token attachment and 401 handling for auth endpoints
    if (
      request.url.includes('/auth/login') ||
      request.url.includes('/auth/refresh') ||
      request.url.includes('/auth/logout')
    ) {
      return next.handle(request).pipe(catchError((error) => this.handleError(error)));
    }

    const token = this.authService.getToken();
    if (token) {
      request = this.addTokenToRequest(request, token);
    }

    return next.handle(request).pipe(
      catchError((error) => {
        if (error instanceof HttpErrorResponse && error.status === 401) {
          return this.handle401Error(request, next);
        }
        return this.handleError(error);
      })
    );
  }

  private addTokenToRequest(request: HttpRequest<any>, token: string): HttpRequest<any> {
    return request.clone({
      setHeaders: {
        Authorization: `Bearer ${token}`,
      },
    });
  }

  private handle401Error(request: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // No access token to refresh with - bail out to the login page. (The refresh
    // token itself is an httpOnly cookie the browser attaches automatically.)
    if (!this.authService.getToken()) {
      this.forceLogin();
      return throwError(() => 'No valid tokens available');
    }

    // Single refresh mechanism lives in AuthService; concurrent 401s share it.
    return from(this.authService.refreshToken()).pipe(
      switchMap((token) => {
        if (token) {
          return next.handle(this.addTokenToRequest(request, token));
        }
        this.forceLogin();
        return throwError(() => 'Session expired. Please log in again.');
      })
    );
  }

  private forceLogin(): void {
    this.authService.clearAuthState();
    this.router.navigate(['/login']);
  }

  private handleError(err: any): Observable<never> {
    // Preserve structured validation errors for form consumers.
    const val = err?.error as ValidationError | undefined;
    if (val && val.errors) {
      return throwError(() => val);
    }
    // Otherwise pass the original error through so genuine failures (500s, network
    // errors, timeouts) remain distinguishable from auth failures at the call site.
    return throwError(() => err);
  }
}

export interface ValidationError {
  message: string;
  errors: FieldError[];
}
export interface FieldError {
  message: string;
  field: string;
}
