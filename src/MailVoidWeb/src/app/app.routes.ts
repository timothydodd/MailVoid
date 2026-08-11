import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './_services/auth.guard';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'mail',
    pathMatch: 'full',
  },
  {
    path: 'login',
    loadComponent: () => import('./Pages/login-page/login-page.component').then((m) => m.LoginPageComponent),
  },
  {
    path: 'mail',
    loadComponent: () => import('./Pages/mail/mail.component').then((m) => m.MailComponent),
    canActivate: [authGuard],
  },
  {
    path: 'mail/:id',
    loadComponent: () => import('./Pages/mail-detail/mail-detail.component').then((m) => m.MailDetailComponent),
    canActivate: [authGuard],
  },
  {
    path: 'hooks',
    loadComponent: () => import('./Pages/hooks/hooks.component').then((m) => m.HooksComponent),
    canActivate: [adminGuard],
  },
  {
    path: 'hooks/:bucket/:id',
    loadComponent: () => import('./Pages/hook-detail/hook-detail.component').then((m) => m.HookDetailComponent),
    canActivate: [adminGuard],
  },
  {
    path: 'error/:errorCode',
    pathMatch: 'full',
    loadComponent: () => import('./Pages/error-page/error-page.component').then((m) => m.ErrorPageComponent),
  },
];
