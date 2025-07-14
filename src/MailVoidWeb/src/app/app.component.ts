import { CommonModule } from '@angular/common';
import { Component, inject, OnInit, OnDestroy } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ModalService } from './_components/modal/modal.service';
import { ValidationDefaultsComponent } from './_components/validation-defaults/validation-defaults.component';
import { AuthService } from './_services/auth-service';
import { MainNavBarComponent } from './Pages/main-nav-bar/main-nav-bar.component';
import { ThemeService } from './_services/theme.service';
import { SignalRService } from './services/signalr.service';
import { MailNotificationComponent } from './_components/mail-notification/mail-notification.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, MainNavBarComponent, ValidationDefaultsComponent, MailNotificationComponent],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'MailVoid';
  modalService = inject(ModalService);
  authService = inject(AuthService);
  themeService = inject(ThemeService);
  signalRService = inject(SignalRService);

  constructor() {
    // Theme service initializes automatically
  }

  ngOnInit(): void {
    // Initialize user data after service is fully constructed
    this.authService.initializeCurrentUser();
    
    // Start SignalR connection when user is authenticated
    this.authService.currentUser$.subscribe(user => {
      if (user) {
        this.signalRService.startConnection();
      } else {
        this.signalRService.stopConnection();
      }
    });
  }

  ngOnDestroy(): void {
    this.signalRService.stopConnection();
  }
}
