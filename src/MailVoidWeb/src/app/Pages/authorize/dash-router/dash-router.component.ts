import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { from } from 'rxjs';
import { catchError, switchMap, take } from 'rxjs/operators';
import { AuthService } from '../../../_services/auth.service';

@Component({
  selector: 'app-dash-router',
  template: '',
})
export class DashRouterComponent {
  private authService = inject(AuthService);
  private router = inject(Router);

  constructor() {
    this.authService
      .checkLogin()
      .pipe(
        take(1),
        switchMap(() => {
          return this.router.navigate([`/mail`]);
        }),
        // Every subscription receives the same shared value
        catchError((err) => {
          if (err instanceof ProgressEvent) return from(this.router.navigate([`/error/408`]));

          return from(this.router.navigate([`/error/500`]));
        })
      )

      .subscribe();
  }
}
