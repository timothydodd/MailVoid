import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { FilterOptions, Mail, MailService } from '../../_services/api/mail.service';
import { BoxListComponent } from './box-list/box-list.component';

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [CommonModule, BoxListComponent],
  template: `
    <div class="container d-flex flex-row flex-grow-1">
      <div class="left-side">
        <h3>Mailboxes</h3>
        <app-box-list
          [mailboxes]="mailboxes()"
          [(selectedBox)]="selectedBox"
          (deleteEvent)="deleteBox($event)"
        ></app-box-list>
      </div>
      <div class="right-side">
        <h3>Emails</h3>
        <table class="table table-striped">
          <thead>
            <tr>
              <th>From</th>
              <th>To</th>
              <th>Subject</th>
              <th>Created On</th>
            </tr>
          </thead>
          <tbody>
            @if (emails()) {
              @for (email of emails(); track email.id) {
                <tr>
                  <td>{{ email.from }}</td>
                  <td>{{ email.to }}</td>
                  <td>
                    <a class="btn-link" (click)="clickMail(email)">{{ email.subject }}</a>
                  </td>
                  <td>{{ email.createdOn | date: 'short' }}</td>
                </tr>
              }
            }
          </tbody>
        </table>
      </div>
    </div>
  `,
  styleUrl: './mail.component.scss',
})
export class MailComponent {
  router = inject(Router);
  mailService = inject(MailService);
  mailboxes = signal<string[] | null>(null);
  emails = signal<Mail[] | null>(null);
  selectedBox = signal<string | null>(null);

  constructor() {
    toObservable(this.selectedBox)
      .pipe(
        switchMap((selectedBox) =>
          this.mailService.getEmails(selectedBox ? ({ to: selectedBox } as FilterOptions) : undefined)
        ),
        takeUntilDestroyed()
      )
      .subscribe((selectedBox) => {
        this.emails.set(selectedBox);
      });
    this.refreshMail();
  }
  refreshMail() {
    this.mailService.getMailboxes().subscribe((mailboxes) => {
      this.mailboxes.set(mailboxes);
    });
  }
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  clickMail(mail: Mail) {
    this.router.navigate(['mail', mail.id]);
  }
  deleteBox(email: string) {
    this.mailService.deleteBoxes({ to: email } as FilterOptions).subscribe(() => {
      this.refreshMail();
    });
  }
}
