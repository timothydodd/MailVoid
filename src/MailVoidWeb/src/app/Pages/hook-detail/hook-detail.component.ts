import { CommonModule } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideAngularModule } from 'lucide-angular';
import { filter, switchMap } from 'rxjs';
import { WebhookDetail, WebhookService } from '../../_services/api/webhook.service';

@Component({
  selector: 'app-hook-detail',
  standalone: true,
  imports: [CommonModule, LucideAngularModule],
  templateUrl: './hook-detail.component.html',
  styleUrl: './hook-detail.component.scss',
})
export class HookDetailComponent {
  webhook = signal<WebhookDetail | null>(null);
  parsedHeaders = signal<Record<string, string> | null>(null);
  parsedBody = signal<string | null>(null);
  isJsonBody = signal(false);

  webhookService = inject(WebhookService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);

  constructor() {
    this.route.paramMap
      .pipe(
        filter((x) => !!x.get('id')),
        switchMap((x) => this.webhookService.getWebhook(Number(x.get('id')))),
        takeUntilDestroyed()
      )
      .subscribe((d) => {
        this.webhook.set(d);

        // Parse headers
        try {
          const headers = JSON.parse(d.headers);
          this.parsedHeaders.set(headers);
        } catch {
          this.parsedHeaders.set(null);
        }

        // Try to pretty-print JSON body
        try {
          const parsed = JSON.parse(d.body);
          this.parsedBody.set(JSON.stringify(parsed, null, 2));
          this.isJsonBody.set(true);
        } catch {
          this.parsedBody.set(d.body);
          this.isJsonBody.set(false);
        }
      });
  }

  goBack() {
    const webhook = this.webhook();
    if (webhook) {
      this.router.navigate(['/hooks'], { queryParams: { bucket: webhook.bucketName } });
    } else {
      this.router.navigate(['/hooks']);
    }
  }

  copyToClipboard(text: string) {
    navigator.clipboard.writeText(text);
  }
}
