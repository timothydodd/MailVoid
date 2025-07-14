import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { SignalRService, MailNotification } from '../../services/signalr.service';
import { trigger, transition, style, animate, state } from '@angular/animations';

interface NotificationItem extends MailNotification {
  show: boolean;
}

@Component({
  selector: 'app-mail-notification',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './mail-notification.component.html',
  styleUrls: ['./mail-notification.component.scss'],
  animations: [
    trigger('slideIn', [
      transition(':enter', [
        style({ transform: 'translateX(100%)', opacity: 0 }),
        animate('300ms ease-out', style({ transform: 'translateX(0)', opacity: 1 }))
      ]),
      transition(':leave', [
        animate('300ms ease-in', style({ transform: 'translateX(100%)', opacity: 0 }))
      ])
    ]),
    trigger('pulse', [
      state('active', style({ transform: 'scale(1)' })),
      state('inactive', style({ transform: 'scale(1)' })),
      transition('inactive => active', [
        animate('200ms ease-in', style({ transform: 'scale(1.05)' })),
        animate('200ms ease-out', style({ transform: 'scale(1)' }))
      ])
    ])
  ]
})
export class MailNotificationComponent implements OnInit, OnDestroy {
  notifications: NotificationItem[] = [];
  private destroy$ = new Subject<void>();
  pulseState = 'inactive';

  constructor(private signalRService: SignalRService) {}

  ngOnInit(): void {
    this.signalRService.newMail$
      .pipe(takeUntil(this.destroy$))
      .subscribe(mail => {
        this.addNotification(mail);
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  private addNotification(mail: MailNotification): void {
    const notification: NotificationItem = { ...mail, show: false };
    this.notifications.unshift(notification);
    
    // Trigger pulse animation
    this.pulseState = 'active';
    setTimeout(() => {
      this.pulseState = 'inactive';
    }, 100);
    
    // Show notification after a small delay for animation
    setTimeout(() => {
      notification.show = true;
    }, 100);
    
    // Remove notification after 5 seconds
    setTimeout(() => {
      notification.show = false;
      setTimeout(() => {
        const index = this.notifications.indexOf(notification);
        if (index > -1) {
          this.notifications.splice(index, 1);
        }
      }, 300);
    }, 5000);
    
    // Keep only last 5 notifications
    if (this.notifications.length > 5) {
      this.notifications = this.notifications.slice(0, 5);
    }
  }

  removeNotification(index: number): void {
    if (this.notifications[index]) {
      this.notifications[index].show = false;
      setTimeout(() => {
        this.notifications.splice(index, 1);
      }, 300);
    }
  }
}