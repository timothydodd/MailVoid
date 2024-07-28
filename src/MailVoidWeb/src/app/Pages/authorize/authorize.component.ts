import { Component, inject, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { from, of } from 'rxjs';
import { catchError, switchMap, take } from 'rxjs/operators';
import { AuthService } from '../../_services/auth.service';
import { ErrorCodes } from '../error-page/error-page.component';

@Component({
  template: '',
  standalone: true,
  imports: [],
})
export class AuthorizeComponent implements OnInit {
  private authService = inject(AuthService);
  private router = inject(Router);

  ngOnInit(): void {
    const params = window.location.search;

    if (params) {
      /// parse errors from auth0
      const urlParams = new URLSearchParams(window.location.search);
      if (urlParams) {
        const errorType = urlParams.get('error');
        const errorDescription = urlParams.get('error_description');
        debugger;
        if (errorType || errorDescription) {
          if (errorDescription === 'user is blocked') {
            this.router.navigate([`/error/` + ErrorCodes.UserBlocked]);
            return;
          }
          if (errorType === 'access_denied') {
            this.router.navigate([`/error/` + ErrorCodes.UserAccess]);
            return;
          }
          this.router.navigate([`/error/` + ErrorCodes.UserLogin]);
          return;
        }
      }
    }

    this.authService.handleRedirectCallback$
      .pipe(
        take(1),
        switchMap((x) => {
          return this.authService.checkLogin().pipe(
            switchMap(() => {
              if (x.appState) {
                if (x.appState.target && x.appState.target !== '/') {
                  return this.router.navigate([x.appState.target]);
                } else {
                  return this.router.navigate([`/mail`]);
                }
              }
              return of(null);
            })
          );
        }),
        catchError((err) => {
          if (err instanceof ProgressEvent) from(this.router.navigate([`/error/408`]));

          return from(this.router.navigate([`/error/500`]));
        })
      )
      .subscribe();
  }
}
