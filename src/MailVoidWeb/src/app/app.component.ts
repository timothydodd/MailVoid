import { CommonModule } from '@angular/common';
import { Component, DestroyRef, inject, OnInit, OnDestroy, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterOutlet } from '@angular/router';
import { ModalContainerService, ToastComponent, ToastService } from '@rd-ui';
import { AuthService } from './_services/auth-service';
import { MainNavBarComponent } from './Pages/main-nav-bar/main-nav-bar.component';
import { ThemeService } from './_services/theme.service';
import { SignalRService } from './services/signalr.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, MainNavBarComponent, ToastComponent],
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
  private toastService = inject(ToastService);
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

    // Surface new mail as a toast via the shared rd-toast (mounted in the template).
    this.signalRService.newMail$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((mail) => {
      this.toastService.info(
        `${mail.subject || '(No Subject)'} — from ${mail.from}`,
        'New Email'
      );
    });
  }

  ngOnDestroy(): void {
    this.signalRService.stopConnection();
  }
}
