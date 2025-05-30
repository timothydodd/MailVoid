import { ApplicationConfig, importProvidersFrom, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { BrowserAnimationsModule, provideAnimations } from '@angular/platform-browser/animations';
import { JwtModule } from '@auth0/angular-jwt';
import { Cog, EllipsisVertical, LucideAngularModule, SquareMenu, User, X } from 'lucide-angular';
import { provideToastr } from 'ngx-toastr';
import { environment } from '../environments/environment';
import { JwtInterceptor } from './_services/jwt-interceptor';
import { routes } from './app.routes';
export function tokenGetter() {
  return localStorage.getItem('authToken');
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideAnimations(),
    provideHttpClient(withInterceptorsFromDi()),
    importProvidersFrom([BrowserAnimationsModule]),
    {
      provide: HTTP_INTERCEPTORS,
      useClass: JwtInterceptor,
      multi: true,
    },
    importProvidersFrom(
      JwtModule.forRoot({
        config: {
          tokenGetter: tokenGetter,
          allowedDomains: undefined, // Update with your API domain
          disallowedRoutes: [`${environment.apiUrl}/api/auth/login`, `${environment.apiUrl}/api/health`], // Update with any routes you want to exclude
        },
      })
    ),
    importProvidersFrom(LucideAngularModule.pick({ EllipsisVertical, X, User, Cog, SquareMenu })),
    provideToastr(),
  ],
};
