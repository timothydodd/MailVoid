import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideDynamicIcon } from '@lucide/angular';
import { map } from 'rxjs';
import { MailViewComponent } from './mail-view/mail-view.component';

/** Full-page message route (/mail/:id). The list normally opens mail in a side panel. */
@Component({
  selector: 'app-mail-detail',
  standalone: true,
  imports: [LucideDynamicIcon, MailViewComponent],
  template: `
    <div class="detail-page">
      <div class="detail-toolbar">
        <button type="button" class="btn-quiet" (click)="goBack()">
          <svg lucideIcon="arrow-left" size="15"></svg>
          Back to mail
        </button>
      </div>
      @if (mailId(); as id) {
        <app-mail-view class="list-card detail-card" [mailId]="id" />
      }
    </div>
  `,
  styleUrl: './mail-detail.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailDetailComponent {
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  mailId = toSignal(this.route.paramMap.pipe(map((p) => p.get('id'))), { initialValue: null });

  goBack() {
    this.router.navigate(['/mail']);
  }
}
