import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
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
    // Old deep links: the detail now opens beside the list.
    path: 'hooks/:bucket/:id',
    redirectTo: ({ params }) =>
      inject(Router).createUrlTree(['/hooks'], { queryParams: { bucket: params['bucket'], id: params['id'] } }),
  },
  {
    path: 'error/:errorCode',
    pathMatch: 'full',
    loadComponent: () => import('./Pages/error-page/error-page.component').then((m) => m.ErrorPageComponent),
  },
];
