import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, firstValueFrom, Observable, shareReplay, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { LastSeenService } from './last-seen.service';
import { MailSearchService } from './mail-search.service';

// The refresh token is held server-side as an httpOnly cookie (set by /api/auth on
// login/refresh), so it is never exposed to JavaScript. Only the short-lived access
// token lives in localStorage; requests that rotate the cookie use withCredentials.
@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private tokenKey = 'authToken';
  private legacyRefreshTokenKey = 'refreshToken';
  private currentUserSubject = new BehaviorSubject<User | null>(null);

  // Single in-flight refresh so concurrent 401s share one refresh request.
  private refreshInFlight?: Promise<string | null>;
  // Cached, shared user request so the user is fetched once, not per-consumer.
  private user$?: Observable<User>;

  private http = inject(HttpClient);
  private router = inject(Router);
  private lastSeenService = inject(LastSeenService);
  private mailSearchService = inject(MailSearchService);

  isLoggedIn = new BehaviorSubject<boolean>(false);
  currentUser$ = this.currentUserSubject.asObservable();

  constructor() {
    const token = this.getToken();
    this.isLoggedIn.next(token !== null);
    // Don't load user data in constructor to avoid circular dependency
    // User data will be loaded when needed or on login
  }
  login(userName: string, password: string) {
    // withCredentials so the browser stores the httpOnly refresh-token cookie.
    return this.http
      .post<LoginResponse>(`${environment.apiUrl}/api/auth/login`, { userName, password }, { withCredentials: true })
      .pipe(
        tap((response) => {
          this.storeToken(response.accessToken);
          this.user$ = undefined;
          this.isLoggedIn.next(true);
          this.loadCurrentUser();
        })
      );
  }
  changePassword(request: ChangePasswordRequest) {
    return this.http.post(`${environment.apiUrl}/api/auth/change-password`, request);
  }
  getUser() {
    if (!this.user$) {
      this.user$ = this.http.get<User>(`${environment.apiUrl}/api/auth/user`).pipe(shareReplay(1));
    }
    return this.user$;
  }
  async logout() {
    // Only call the logout API if we have a valid token
    if (this.isAuthenticated()) {
      try {
        // withCredentials so the server can clear the refresh-token cookie.
        await firstValueFrom(this.http.post(`${environment.apiUrl}/api/auth/logout`, {}, { withCredentials: true }));
      } catch (error) {
        // Ignore errors during logout - we're logging out anyway
        console.warn('Logout API call failed, but continuing with local logout:', error);
      }
    }

    this.clearAuthState();
    this.router.navigate(['/login']);
  }

  getToken(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  isAuthenticated(): boolean {
    const token = this.getToken();
    return token !== null && !this.isTokenExpired();
  }

  getCurrentUserId(): string | null {
    const token = this.getToken();
    if (!token) return null;

    try {
      const payload = this.getTokenPayload(token);
      // Try multiple possible claim names for user ID
      return (
        payload?.sub ||
        payload?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier'] ||
        payload?.Sub ||
        null
      );
    } catch (error) {
      console.error('Error parsing token:', error);
      return null;
    }
  }

  getCurrentUsername(): string | null {
    const token = this.getToken();
    if (!token) return null;

    try {
      const payload = this.getTokenPayload(token);
      // Use standard claim for name which contains username
      return payload?.['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'] || payload?.name || null;
    } catch (error) {
      console.error('Error parsing token:', error);
      return null;
    }
  }

  getUserRole(): string | null {
    const token = this.getToken();
    if (!token) return null;

    try {
      const payload = this.getTokenPayload(token);
      // Use standard claim for role
      if (!payload) return null;
      return payload?.['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || payload?.role || null;
    } catch (error) {
      console.error('Error parsing token:', error);
      return null;
    }
  }

  isTokenExpired(): boolean {
    const token = this.getToken();
    if (!token) return true;

    try {
      const payload = this.getTokenPayload(token);
      if (!payload?.exp) return true;

      const currentTime = Math.floor(Date.now() / 1000);
      return payload.exp < currentTime;
    } catch (error) {
      console.error('Error parsing token:', error);
      return true;
    }
  }

  private getTokenPayload(token: string): any {
    if (!token) return null;

    const parts = token.split('.');
    if (parts.length !== 3) {
      throw new Error('Invalid JWT token format');
    }

    try {
      const payload = parts[1];
      // Add padding if needed for base64 decoding
      const padded = payload + '='.repeat((4 - (payload.length % 4)) % 4);
      return JSON.parse(atob(padded));
    } catch (error) {
      throw new Error('Failed to decode JWT payload');
    }
  }

  isAdmin(): boolean {
    try {
      const role = this.getUserRole();
      return role?.toLowerCase() === 'admin';
    } catch (error) {
      console.error('Error checking admin role:', error);
      return false;
    }
  }

  getSubdomain(): string | null {
    const token = this.getToken();
    if (!token) return null;

    try {
      const payload = this.getTokenPayload(token);
      return payload?.subdomain || null;
    } catch (error) {
      return null;
    }
  }

  private storeToken(accessToken: string) {
    localStorage.setItem(this.tokenKey, accessToken);
  }

  private clearTokens() {
    localStorage.removeItem(this.tokenKey);
    // Remove any refresh token left by an older build; it now lives in an httpOnly cookie.
    localStorage.removeItem(this.legacyRefreshTokenKey);
  }

  // Method to clear auth state without API call (for error scenarios).
  // Clears ALL per-user app state so a shared machine doesn't leak the previous
  // user's mailbox paths / search terms.
  clearAuthState() {
    this.clearTokens();
    this.user$ = undefined;
    this.refreshInFlight = undefined;
    this.lastSeenService.clearLastSeenData();
    this.mailSearchService.clear();
    this.isLoggedIn.next(false);
    this.currentUserSubject.next(null);
  }

  private loadCurrentUser() {
    this.getUser().subscribe({
      next: (user) => this.currentUserSubject.next(user),
      error: (error) => {
        console.error('Failed to load current user:', error);
        this.currentUserSubject.next(null);
      },
    });
  }

  // Method to initialize user data after service is fully constructed
  public initializeCurrentUser() {
    const token = this.getToken();
    if (token !== null && !this.isTokenExpired()) {
      this.loadCurrentUser();
    }
  }

  // Single refresh mechanism: concurrent callers share one in-flight request.
  refreshToken(): Promise<string | null> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }

    const accessToken = this.getToken();
    if (!accessToken) {
      this.clearAuthState();
      return Promise.resolve(null);
    }

    this.refreshInFlight = this.doRefresh(accessToken).finally(() => {
      this.refreshInFlight = undefined;
    });
    return this.refreshInFlight;
  }

  private async doRefresh(accessToken: string): Promise<string | null> {
    try {
      // The refresh token rides along as an httpOnly cookie (withCredentials); only
      // the expired access token is sent in the body so the server can identify the user.
      const response = await firstValueFrom(
        this.http.post<LoginResponse>(
          `${environment.apiUrl}/api/auth/refresh`,
          { accessToken },
          { withCredentials: true }
        )
      );
      if (response) {
        this.storeToken(response.accessToken);
        this.isLoggedIn.next(true);
        return response.accessToken;
      }
      this.clearAuthState();
      return null;
    } catch (error) {
      console.error('Token refresh failed:', error);
      this.clearAuthState();
      return null;
    }
  }
}

export interface User {
  id: number;
  userName: string;
}

export interface ChangePasswordRequest {
  oldPassword: string;
  newPassword: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresIn: number;
}
