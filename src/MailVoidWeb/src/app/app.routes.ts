import { Routes } from '@angular/router';
import { authGuard } from './_services/auth.guard';
import { ErrorPageComponent } from './Pages/error-page/error-page.component';
import { HookDetailComponent } from './Pages/hook-detail/hook-detail.component';
import { HooksComponent } from './Pages/hooks/hooks.component';
import { LoginPageComponent } from './Pages/login-page/login-page.component';
import { MailDetailComponent } from './Pages/mail-detail/mail-detail.component';
import { MailComponent } from './Pages/mail/mail.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'mail',
    pathMatch: 'full',
  },
  {
    path: 'login',
    component: LoginPageComponent,
  },
  {
    path: 'mail',
    component: MailComponent,
    canActivate: [authGuard],
  },
  {
    path: 'mail/:id',
    component: MailDetailComponent,
    canActivate: [authGuard],
  },
  {
    path: 'hooks',
    component: HooksComponent,
    canActivate: [authGuard],
  },
  {
    path: 'hooks/:bucket/:id',
    component: HookDetailComponent,
    canActivate: [authGuard],
  },
  { path: 'error/:errorCode', pathMatch: 'full', component: ErrorPageComponent },
];
