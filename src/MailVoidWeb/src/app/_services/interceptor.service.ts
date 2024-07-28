import { HttpEvent, HttpHandler, HttpHeaders, HttpInterceptor, HttpParams, HttpRequest } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, throwError } from 'rxjs';
import { catchError, mergeMap } from 'rxjs/operators';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

@Injectable()
export class InterceptorService implements HttpInterceptor {
  constructor(private auth: AuthService) {}

  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    if (req.url.startsWith(environment.apiUrl)) {
      //Custom Parameters that allow special http request features
      let customParams: InterceptorHttpParams | null = null;

      if (req.params && req.params instanceof InterceptorHttpParams) {
        customParams = req.params as InterceptorHttpParams;
      }
      if (customParams?.interceptorConfig?.noToken === true) return next.handle(req);

      return this.auth
        .getTokenSilently$()

        .pipe(
          mergeMap((token) => {
            let headers = new HttpHeaders();
            headers = headers.append('Authorization', `Bearer ${token}`);

            const tokenReq = req.clone({
              headers,
            });
            return next.handle(tokenReq).pipe(
              catchError((err) => {
                if (err.status === 401) {
                  // auto logout if 401 response returned from api
                  this.auth.logout();
                }

                let error = '';
                if (err) {
                  if (err.error) {
                    const val = err.error as ValidationError;

                    if (val) {
                      return throwError(() => val);
                    }
                  }
                  if (err.message) {
                    error = err.message;
                  } else if (err.error && err.error.message) {
                    error = err.error.message || err.statusText;
                  }
                }
                return throwError(error);
              })
            );
          }),
          catchError((err) => throwError(() => err))
        );
    }
    return next.handle(req);
  }
}
export class InterceptorHttpParams extends HttpParams {
  constructor(
    public interceptorConfig: { ignoreAdmin: boolean; noToken: boolean },
    params?: { [param: string]: string | string[] }
  ) {
    super({ fromObject: params });
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
