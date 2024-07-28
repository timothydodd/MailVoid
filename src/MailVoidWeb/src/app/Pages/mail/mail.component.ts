import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { switchMap } from 'rxjs';
import { FilterOptions, Mail, MailService } from '../../_services/api/mail.service';

@Component({
  selector: 'app-mail',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './mail.component.html',
  styleUrl: './mail.component.scss'
})
export class MailComponent {
  router = inject(Router);
  mailService = inject(MailService);
  mailboxes = signal<string[] |null>(null);
  emails = signal<Mail[] |null>(null);
  selectedBox = signal<string |null>(null);


  constructor(){
    this.mailService.getMailboxes().subscribe((mailboxes) => {
      this.mailboxes.set(mailboxes);
    });
    toObservable(this.selectedBox).pipe(switchMap((selectedBox) => this.mailService.getEmails( selectedBox? {to: selectedBox} as FilterOptions:undefined)),
      takeUntilDestroyed())
    .subscribe((selectedBox) => {
      this.emails.set(selectedBox);
    });
  }
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  clickMail(mail: Mail) {
    this.router.navigate(['mail', mail.id]);
  }
}

