import { ApplicationConfig, importProvidersFrom, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import localeEn from '@angular/common/locales/en';

import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi, withXhr } from '@angular/common/http';
import { JwtModule } from '@auth0/angular-jwt';
import {
  provideLucideIcons,
  LucideArrowLeft,
  LucideCalendar,
  LucideCircleCheck,
  LucideCircleX,
  LucideChevronDown,
  LucideChevronLeft,
  LucideChevronRight,
  LucideChevronUp,
  LucideClock,
  LucideCog,
  LucideCopy,
  LucideCrown,
  LucideDownload,
  LucideSquarePen,
  LucideEllipsisVertical,
  LucideEye,
  LucideEyeOff,
  LucideFile,
  LucideFolder,
  LucideInbox,
  LucideInfo,
  LucideLoaderCircle,
  LucideLock,
  LucideLogOut,
  LucideMail,
  LucideMenu,
  LucideMoon,
  LucidePaperclip,
  LucidePencil,
  LucidePlus,
  LucideCirclePlus,
  LucideSearch,
  LucideSquareMenu,
  LucideSun,
  LucideTrash,
  LucideShare2,
  LucideTrash2,
  LucideUpload,
  LucideUser,
  LucideUsers,
  LucideWebhook,
  LucideX,
} from '@lucide/angular';
import { environment } from '../environments/environment';
import { JwtInterceptor } from './_services/jwt-interceptor';
import { routes } from './app.routes';
export function tokenGetter() {
  return localStorage.getItem('authToken');
}

registerLocaleData(localeEn);

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(withXhr(), withInterceptorsFromDi()),
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
    provideLucideIcons(
      LucideEllipsisVertical,
      LucideX,
      LucideUser,
      LucideCog,
      LucideSquareMenu,
      LucideInbox,
      LucideChevronDown,
      LucideChevronUp,
      LucideChevronLeft,
      LucideChevronRight,
      LucideCirclePlus,
      LucideSearch,
      LucidePencil,
      LucideShare2,
      LucideTrash2,
      LucideUpload,
      LucideInfo,
      LucideMail,
      LucideFile,
      LucideFolder,
      LucideDownload,
      LucideCalendar,
      LucideSquarePen,
      LucideEye,
      LucideEyeOff,
      LucideUsers,
      LucidePlus,
      LucideTrash,
      LucideMenu,
      LucideArrowLeft,
      LucideCrown,
      LucideLock,
      LucideSun,
      LucideMoon,
      LucidePaperclip,
      LucideClock,
      LucideLoaderCircle,
      LucideLogOut,
      LucideCircleCheck,
      LucideCircleX,
      LucideCopy,
      LucideWebhook
    ),
    { provide: LOCALE_ID, useValue: 'en-US' },
  ],
};
