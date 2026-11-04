import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { MailSortColumn, MailSortDirection, MailWithReadStatus } from '../../../_services/api/mail.service';
import { initialsFor, shortMailDate } from '../../../_services/mail-format';

interface Column {
  key: MailSortColumn;
  label: string;
}

/** Message rows with sortable column headers. Purely presentational. */
@Component({
  selector: 'app-mail-list',
  standalone: true,
  imports: [DatePipe, LucideDynamicIcon],
  templateUrl: './mail-list.component.html',
  styleUrl: './mail-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailListComponent {
  emails = input<MailWithReadStatus[] | null>(null);
  loading = input(false);
  error = input(false);
  /** The "To" column is redundant when a single mailbox is selected. */
  showTo = input(true);
  selectedBox = input<string | null>(null);
  sortColumn = input<MailSortColumn>('createdOn');
  sortDirection = input<MailSortDirection>('desc');
  openId = input<number | null>(null);

  sort = output<MailSortColumn>();
  open = output<MailWithReadStatus>();

  columns = computed<Column[]>(() => [
    { key: 'from', label: 'From' },
    ...(this.showTo() ? [{ key: 'to' as const, label: 'To' }] : []),
    { key: 'subject', label: 'Subject' },
    { key: 'createdOn', label: 'Received' },
  ]);

  initials = initialsFor;
  shortDate = shortMailDate;
}
