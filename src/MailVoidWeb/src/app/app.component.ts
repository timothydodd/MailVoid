import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, OnDestroy, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { ModalContainerService, ToastComponent } from '@rd-ui';
import { AuthService } from './_services/auth-service';
import { MailToastComponent } from './_components/mail-toast/mail-toast.component';
import { MainNavBarComponent } from './Pages/main-nav-bar/main-nav-bar.component';
import { ThemeService } from './_services/theme.service';
import { SignalRService } from './services/signalr.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, MainNavBarComponent, ToastComponent, MailToastComponent],
  templateUrl: './app.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.component.scss',
})
export class AppComponent implements OnInit, OnDestroy {
  title = 'MailVoid';
  modalContainerService = inject(ModalContainerService);
  authService = inject(AuthService);
  themeService = inject(ThemeService);
  signalRService = inject(SignalRService);
  private destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    // Initialize user data after service is fully constructed
    this.authService.initializeCurrentUser();

    // Start SignalR connection when user is authenticated
    this.authService.currentUser$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((user) => {
      if (user) {
        this.signalRService.startConnection();
      } else {
        this.signalRService.stopConnection();
      }
    });
    // New mail is surfaced by <app-mail-toast> (theme-aware, clickable to open).
  }

  ngOnDestroy(): void {
    this.signalRService.stopConnection();
  }
}
