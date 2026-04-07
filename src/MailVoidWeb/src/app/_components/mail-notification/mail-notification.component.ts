import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
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

  constructor(private signalRService: SignalRService, private router: Router) {}

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

    // Remove notification after 30 seconds
    setTimeout(() => {
      notification.hiding = true;
      setTimeout(() => {
        const index = this.notifications.indexOf(notification);
        if (index > -1) {
          this.notifications.splice(index, 1);
        }
      }, 300);
    }, 30000);

    // Keep only last 5 notifications
    if (this.notifications.length > 5) {
      this.notifications = this.notifications.slice(0, 5);
    }
  }

  openNotification(index: number): void {
    const notification = this.notifications[index];
    if (notification) {
      this.router.navigate(['mail', notification.id]);
      notification.hiding = true;
      setTimeout(() => {
        const i = this.notifications.indexOf(notification);
        if (i > -1) {
          this.notifications.splice(i, 1);
        }
      }, 300);
    }
  }
}