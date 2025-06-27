import { CommonModule } from '@angular/common';
import { Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { filter, map, switchMap, take } from 'rxjs';
import { Mail, MailService } from '../../_services/api/mail.service';

@Component({
  selector: 'app-mail-detail',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  templateUrl: './mail-detail.component.html',
  styleUrl: './mail-detail.component.scss',
})
export class MailDetailComponent {
  mail = signal<Mail | null>(null);
  mailService = inject(MailService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  emailContent = viewChild<ElementRef<HTMLIFrameElement>>('emailContent');
  constructor() {
    this.route.paramMap
      .pipe(
        filter((x) => !!x.get('id')),
        switchMap((x) => this.mailService.getEmail(<string>x.get('id'), true)),
        takeUntilDestroyed()
      )
      .subscribe((d) => {
        this.mail.set(d);
      });
    var mailItem = toObservable(this.mail).pipe(takeUntilDestroyed());

    toObservable(this.emailContent)
      .pipe(
        filter((x) => !!x),
        take(1),
        switchMap((emailContent) => {
          return mailItem.pipe(
            filter((x) => !!x),
            take(1),
            map((x) => {
              return {
                emailContent,
                mail: x,
              };
            })
          );
        }),
        takeUntilDestroyed()
      )

      .subscribe((x) => {
        var emailContent = x?.emailContent.nativeElement;
        if (x.mail?.isHtml) {
          emailContent?.contentWindow?.document.open();
          emailContent?.contentWindow?.document.write(x.mail.text);
          emailContent?.contentWindow?.document.close();
        }
      });
  }

  goBack() {
    this.router.navigate(['/mail']);
  }
}
