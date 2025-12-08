import { ApplicationConfig, importProvidersFrom, LOCALE_ID, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';
import { registerLocaleData } from '@angular/common';
import localeEn from '@angular/common/locales/en';

import { HTTP_INTERCEPTORS, provideHttpClient, withInterceptorsFromDi } from '@angular/common/http';
import { BrowserAnimationsModule, provideAnimations } from '@angular/platform-browser/animations';
import { JwtModule } from '@auth0/angular-jwt';
import {
  ArrowLeft,
  Calendar,
  CheckCircle,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  Clock,
  Cog,
  Copy,
  Crown,
  Edit,
  EllipsisVertical,
  Folder,
  Inbox,
  Info,
  Loader2,
  Lock,
  LucideAngularModule,
  Mail,
  Menu,
  Moon,
  Pencil,
  Plus,
  PlusCircle,
  SquareMenu,
  Sun,
  Trash,
  Trash2,
  UploadIcon,
  User,
  Users,
  Webhook,
  X,
} from 'lucide-angular';
import { provideToastr } from 'ngx-toastr';
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
    importProvidersFrom(
      LucideAngularModule.pick({
        EllipsisVertical,
        X,
        User,
        Cog,
        SquareMenu,
        Inbox,
        ChevronDown,
        ChevronUp,
        ChevronLeft,
        ChevronRight,
        PlusCircle,
        Pencil,
        Trash2,
        UploadIcon,
        Info,
        Mail,
        Folder,
        Calendar,
        Edit,
        Users,
        Plus,
        Trash,
        Menu,
        ArrowLeft,
        Crown,
        Lock,
        Sun,
        Moon,
        Clock,
        Loader2,
        CheckCircle,
        Copy,
        Webhook,
      })
    ),
    provideToastr(),
    { provide: LOCALE_ID, useValue: 'en-US' },
  ],
};
