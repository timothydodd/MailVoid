import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { catchError, of, switchMap } from 'rxjs';
import { MailSettingsModalComponent } from '../../_components/mail-settings-modal/mail-settings-modal.component';
import { FilterOptions, Mail, MailBoxGroups, MailService } from '../../_services/api/mail.service';
import { BoxListComponent } from './box-list/box-list.component';

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [CommonModule, BoxListComponent, LucideAngularModule, MailSettingsModalComponent],
  template: `
    <div class="container d-flex flex-row flex-grow">
      <div class="left-side">
        <div class="section-card">
          <div class="section-header">
            <button class="btn btn-icon align-self-end" (click)="mailSettings.show()">
              <lucide-icon name="cog"></lucide-icon>
            </button>
          </div>
          <div class="section-body">
            <app-box-list
              [mailboxes]="mailboxes()"
              [(selectedBox)]="selectedBox"
              (deleteEvent)="deleteBox($event)"
            ></app-box-list>
          </div>
        </div>
      </div>
      <div class="right-side ">
        <div class="table-wrap">
          <table>
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
                  <tr (click)="clickMail(email)">
                    <td>{{ email.from }}</td>
                    <td>{{ email.to }}</td>
                    <td>
                      {{ email.subject }}
                    </td>
                    <td>{{ email.createdOn | date: 'short' }}</td>
                  </tr>
                }
              }
            </tbody>
          </table>
        </div>
      </div>
    </div>
    <app-mail-settings-modal #mailSettings></app-mail-settings-modal>
  `,
  styleUrl: './mail.component.scss',
})
export class MailComponent {
  router = inject(Router);
  mailService = inject(MailService);
  mailboxes = signal<MailBoxGroups[] | null>(null);
  emails = signal<Mail[] | null>(null);
  selectedBox = signal<string | null>(null);

  constructor() {
    toObservable(this.selectedBox)
      .pipe(
        switchMap((selectedBox) =>
          this.mailService
            .getEmails(selectedBox ? ({ to: selectedBox } as FilterOptions) : undefined)
            .pipe(catchError(() => of({ items: null, totalCount: 0 })))
        ),
        takeUntilDestroyed()
      )
      .subscribe((selectedBox) => {
        this.emails.set(selectedBox?.items);
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
