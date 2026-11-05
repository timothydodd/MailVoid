import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { WebhookListItem } from '../../../_services/api/webhook.service';
import { shortMailDate } from '../../../_services/mail-format';

export type HookSortColumn = 'httpMethod' | 'path' | 'contentType' | 'createdOn';

/** Captured webhooks as a sortable grid list; rows open the detail panel. */
@Component({
  selector: 'app-hook-list',
  standalone: true,
  imports: [DatePipe, LucideDynamicIcon],
  templateUrl: './hook-list.component.html',
  styleUrl: './hook-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HookListComponent {
  webhooks = input<WebhookListItem[] | null>(null);
  loading = input(false);
  error = input(false);
  // In the "All" view each row also names its bucket.
  showBucket = input(false);
  sortColumn = input<HookSortColumn>('createdOn');
  sortDirection = input<'asc' | 'desc'>('desc');
  openId = input<number | null>(null);

  sort = output<HookSortColumn>();
  open = output<number>();

  readonly columns: { key: HookSortColumn; label: string }[] = [
    { key: 'httpMethod', label: 'Method' },
    { key: 'path', label: 'Path' },
    { key: 'contentType', label: 'Content type' },
    { key: 'createdOn', label: 'Received' },
  ];

  shortDate = shortMailDate;
}
