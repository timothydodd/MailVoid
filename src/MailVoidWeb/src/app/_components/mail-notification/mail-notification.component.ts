import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Subject, takeUntil } from 'rxjs';
import { SignalRService, MailNotification } from '../../services/signalr.service';

interface NotificationItem extends MailNotification {
  hiding: boolean;
}

@Component({
  selector: 'app-mail-notification',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './mail-notification.component.html',
  styleUrls: ['./mail-notification.component.scss'],
})
export class MailNotificationComponent implements OnInit, OnDestroy {
  notifications: NotificationItem[] = [];
  private destroy$ = new Subject<void>();

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
    const notification: NotificationItem = { ...mail, hiding: false };
    this.notifications.unshift(notification);

    // Remove notification after 5 seconds
    setTimeout(() => {
      notification.hiding = true;
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
      this.notifications[index].hiding = true;
      setTimeout(() => {
        this.notifications.splice(index, 1);
      }, 300);
    }
  }
}