import { Routes } from '@angular/router';
import { AuthGuard } from './_services/auth.guard';
import { AuthorizeComponent } from './Pages/authorize/authorize.component';
import { ErrorPageComponent } from './Pages/error-page/error-page.component';
import { MailDetailComponent } from './Pages/mail-detail/mail-detail.component';
import { MailComponent } from './Pages/mail/mail.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'mail',
    pathMatch: 'full',
  },
  { path: 'authorize', pathMatch: 'full', component: AuthorizeComponent },
  {
    path: 'mail',
    canActivate: [AuthGuard],
    component: MailComponent,
  },
  {
    path: 'mail/:id',
    canActivate: [AuthGuard],
    component: MailDetailComponent,
  },
  { path: 'error/:errorCode', pathMatch: 'full', component: ErrorPageComponent },
];
