import { AfterViewInit, Component, inject, signal, viewChild } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { tap } from 'rxjs';
import { LoginComponent } from './_components/login/login.component';
import { ModalComponent } from './_components/modal/modal.component';
import { ModalService } from './_components/modal/modal.service';
import { ValidationDefaultsComponent } from './_components/validation-defaults/validation-defaults.component';
import { AuthService } from './_services/auth-service';
import { MainNavBarComponent } from './Pages/main-nav-bar/main-nav-bar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, LoginComponent, MainNavBarComponent, ValidationDefaultsComponent, ModalComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements AfterViewInit {
  title = 'MailVoid';
  loginModal = viewChild<LoginComponent>('loginModal');
  modalService = inject(ModalService);
  private authService = inject(AuthService);

  loggedIn = signal(false);
  constructor() {}
  ngAfterViewInit(): void {
    this.authService.isLoggedIn
      .pipe(
        tap((loggedIn) => {
          this.loggedIn.update(() => loggedIn);
          const modal = this.loginModal();

          if (loggedIn !== true && modal) {
            modal.show();
          }
        })
      )
      .subscribe();
  }
}
