import { inject, Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, RouterStateSnapshot, UrlTree } from '@angular/router';

import { Observable, of } from 'rxjs';
import { filter, map, switchMap, take, timeout } from 'rxjs/operators';
import { AuthService } from './auth.service';

@Injectable({
  providedIn: 'root',
})
export class AuthGuard {
  private authService = inject(AuthService);
  canActivate(
    next: ActivatedRouteSnapshot,
    state: RouterStateSnapshot
  ): Observable<boolean | UrlTree> | Promise<boolean | UrlTree> | boolean {
    return this.authService.isAuthenticated$.pipe(
      switchMap((x) => {
        if (x === true) {
          // the appUser pull is used to make sure that tenant overrides
          return this.authService.appUser.pipe(
            filter((z) => !!z),
            take(1),
            timeout(5000),
            switchMap(() => {
              return of(true);
            })
          );
        } else
          return this.authService.login(state.url).pipe(
            map(() => {
              return false;
            })
          );
      })
    );
  }
}
