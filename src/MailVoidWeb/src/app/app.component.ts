import { CommonModule } from '@angular/common';
import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ModalComponent } from './_components/modal/modal.component';
import { ModalService } from './_components/modal/modal.service';
import { ValidationDefaultsComponent } from './_components/validation-defaults/validation-defaults.component';
import { AuthService } from './_services/auth-service';
import { MainNavBarComponent } from './Pages/main-nav-bar/main-nav-bar.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, MainNavBarComponent, ValidationDefaultsComponent, ModalComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent {
  title = 'MailVoid';
  modalService = inject(ModalService);
  authService = inject(AuthService);

  constructor() {}
}
