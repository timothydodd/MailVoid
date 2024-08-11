import { Routes } from '@angular/router';
import { ErrorPageComponent } from './Pages/error-page/error-page.component';
import { MailDetailComponent } from './Pages/mail-detail/mail-detail.component';
import { MailComponent } from './Pages/mail/mail.component';

export const routes: Routes = [
  {
    path: '',
    redirectTo: 'mail',
    pathMatch: 'full',
  },
  {
    path: 'mail',
    component: MailComponent,
  },
  {
    path: 'mail/:id',
    component: MailDetailComponent,
  },
  { path: 'error/:errorCode', pathMatch: 'full', component: ErrorPageComponent },
];
