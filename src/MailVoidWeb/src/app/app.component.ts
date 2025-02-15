import { Component, inject, signal } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NgbModal, NgbModule } from '@ng-bootstrap/ng-bootstrap';
import { LoginComponent } from './_components/login/login.component';
import { UserMenuComponent } from './_components/user-menu/user-menu.component';
import { ValidationDefaultsComponent } from './_components/validation-defaults/validation-defaults.component';
import { AuthService } from './_services/auth-service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, LoginComponent, NgbModule, UserMenuComponent, UserMenuComponent, ValidationDefaultsComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  title = 'MailVoid';

  modalService = inject(NgbModal);
  private authService = inject(AuthService);

  loggedIn = signal(false);
  constructor() {
    this.authService.isLoggedIn.subscribe((loggedIn) => {
      this.loggedIn.update(() => loggedIn);
      if (!loggedIn) {
        LoginComponent.showModal(this.modalService);
      }
    });
  }
}
