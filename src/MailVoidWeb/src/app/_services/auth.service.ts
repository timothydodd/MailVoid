import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Auth0Client, createAuth0Client } from '@auth0/auth0-spa-js';
import { BehaviorSubject, from, Observable, of, throwError } from 'rxjs';
import { catchError, concatMap, filter, map, shareReplay, switchMap, tap } from 'rxjs/operators';
import { environment } from '../../environments/environment';

type NewType = any;

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);

  isLoggedIn = new BehaviorSubject<boolean>(false);
  appUser = new BehaviorSubject<AppUser | null>(null);
  permission: UserPermission | undefined;
  auth0Client$ = (
    from(
      createAuth0Client({
        domain: environment.auth0.domain,
        clientId: environment.auth0.client_id,
        authorizationParams: {
          redirect_uri: `${window.location.origin}/authorize`,
          audience: environment.auth0.audience,
          display: 'popup',
        },
        useRefreshTokens: true,
        useRefreshTokensFallback: true,
      })
    ) as Observable<Auth0Client>
  )
    .pipe(
      catchError((err) => {
        console.log(err);
        return throwError(err);
      })
    )
    .pipe(
      filter((x) => !!x),
      shareReplay(1)
    );

  isAuthenticated$ = this.auth0Client$.pipe(
    switchMap((client: Auth0Client) => {
      return from(client.isAuthenticated());
    }),
    tap((x) => {
      this.isLoggedIn.next(x);
    })
  );
  handleRedirectCallback$ = this.auth0Client$.pipe(
    switchMap((client: Auth0Client) => {
      return from(client.handleRedirectCallback());
    })
  );

  getAppUser(): Observable<AppUser> {
    return this.appUser.pipe(filter((x) => !!x));
  }

  private getUser(): Observable<AppUser | null> {
    // we first need to get user profile
    return this.auth0Client$.pipe(
      concatMap((client) =>
        from(client.getUser()).pipe(
          map((u) => {
            return { user: u };
          })
        )
      ),
      concatMap(({ user }) => {
        if (!user) {
          return of(null);
        }
        const usr = {} as AppUser;
        usr.email = user.email ?? '';

        usr.avatarUrl = user.picture ?? '';
        usr.externalAuthId = user.sub ?? '';

        usr.firstName = user.name ?? '';
        usr.lastName = '';
        usr.personId = ''; // user['https://void.dbmk2.com/ortho_person_id'];
        this.appUser.next(usr);
        return of(usr);
      })
    );
  }

  checkLogin(): Observable<AppUser | null> {
    // This should only be called on app initialization
    // Set up local authentication streams
    return this.isAuthenticated$.pipe(
      concatMap((loggedIn: boolean) => {
        if (loggedIn === true) {
          // If authenticated, get user and set in app
          // NOTE: you could pass options here if needed
          return this.getUser();
        }
        // If not authenticated, return stream that emits 'false'
        return of(null);
      })
    );
  }
  getTokenSilently$(options?: NewType): Observable<string | void> {
    return this.auth0Client$.pipe(switchMap((client: Auth0Client) => from(client.getTokenSilently(options)))).pipe(
      concatMap((x: any) => {
        const token = x?.access_token ? x.access_token : x;
        return of(token);
      }),
      catchError((err) => {
        if (err.error === 'login_required') {
          return this.login();
        }

        return throwError(() => err);
      })
    );
  }
  getIdTokenClaims$(): Observable<any> {
    return this.auth0Client$.pipe(concatMap((client: Auth0Client) => from(client.getIdTokenClaims())));
  }

  login(redirectPath: string = '/') {
    // A desired redirect path can be passed to login method
    // (e.g., from a route guard)
    // Ensure Auth0 client instance exists
    return this.auth0Client$.pipe(
      switchMap((client: Auth0Client) => {
        // Call method to log in
        return from(
          client.loginWithRedirect({
            authorizationParams: {
              redirect_uri: `${window.location.origin}/authorize`,
            },
            appState: { target: redirectPath },
          })
        );
      })
    );
  }

  logout() {
    // Ensure Auth0 client instance exists
    return this.auth0Client$.subscribe((client: Auth0Client) => {
      // this.userService.removeCurrentUser();
      // Call method to log out
      client.logout({
        clientId: environment.auth0.client_id,
        logoutParams: {
          returnTo: `${window.location.origin}`,
        },
      });
    });
  }

  clearSession() {
    // Ensure Auth0 client instance exists
    this.auth0Client$.subscribe((client: Auth0Client) => {
      // this.userService.removeCurrentUser();
      // Call method to log out
      client.logout({
        logoutParams: {
          federated: true,
        },
      });
    });
  }

  hasPermission(requirement: string) {
    return this.getAppUser().pipe(
      map((user) => {
        return user.permission.permissions.indexOf(requirement) >= 0;
      })
    );
  }
}

export interface UserPermission {
  role: string;
  permissions: string[];
}
export interface AppUser {
  externalAuthId: string;
  personId: string;
  email: string;
  firstName: string;
  lastName: string;
  isActive: boolean;
  isInvite: boolean;
  avatarId: string;
  avatarUrl: string;
  permission: UserPermission;
  customData: any;
}
