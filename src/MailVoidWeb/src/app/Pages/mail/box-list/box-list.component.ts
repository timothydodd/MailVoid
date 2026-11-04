import { ChangeDetectionStrategy, Component, input, model, output, signal } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { MailBoxGroups } from '../../../_services/api/mail.service';
import { BoxMenuComponent } from './box-menu/box-menu.component';
import { splitAddress } from '../../../_services/mail-format';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [LucideDynamicIcon, BoxMenuComponent],
  template: `
    @if (mailboxes(); as mb) {
      @for (group of mb; track group.groupName) {
        <section class="side-section">
          <div class="side-section-label">
            <span class="side-section-title">{{ group.groupName }}</span>
            <span class="side-section-meta" [title]="group.isOwner ? 'You own this group' : ''">
              {{ group.isPublic ? 'Shared' : 'Private' }}
            </span>
          </div>
          @for (item of group.mailBoxes; track (item.path ?? '') + '|' + item.name) {
            <div class="side-item box-row" [class.selected]="selectedBox() === item.name">
              <button
                type="button"
                class="box-select"
                (click)="clickBox(item.name)"
                [title]="item.mailBoxName ? item.name + ' · ' + item.mailBoxName : item.name"
              >
                <span class="side-item-label" [class.has-unread]="item.unreadCount > 0">
                  {{ split(item.name).local
                  }}<span class="box-domain">{{ split(item.name).domain ? '@' + split(item.name).domain : '' }}</span>
                </span>
              </button>
              <button
                type="button"
                class="icon-btn icon-btn-sm box-hover"
                (click)="copyEmail(item.name)"
                [attr.aria-label]="copiedBox() === item.name ? 'Copied' : 'Copy ' + item.name"
                [title]="copiedBox() === item.name ? 'Copied!' : 'Copy address'"
              >
                <svg [lucideIcon]="copiedBox() === item.name ? 'circle-check' : 'copy'" size="14"></svg>
              </button>
              @if (item.unreadCount > 0) {
                <span class="count-pill">{{ item.unreadCount }}</span>
              }
              <app-box-menu
                class="box-hover"
                [item]="item.name"
                [groupName]="group.groupName"
                [unreadCount]="item.unreadCount"
                (deleteEvent)="deleteEvent.emit($event)"
                (markAllAsReadEvent)="markAllAsReadEvent.emit($event)"
              ></app-box-menu>
            </div>
          }
        </section>
      } @empty {
        <p class="box-empty">No mailboxes yet. Mail sent to a test domain shows up here.</p>
      }
    }
  `,
  styleUrl: './box-list.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BoxListComponent {
  mailboxes = input.required<MailBoxGroups[] | null>();
  selectedBox = model<string | null>();
  deleteEvent = output<string>();
  markAllAsReadEvent = output<string>();
  boxClick = output<string | null>();

  copiedBox = signal<string | null>(null);
  split = splitAddress;
  private copiedTimer?: ReturnType<typeof setTimeout>;

  clickBox(box: string | null) {
    this.selectedBox.set(box);
    this.boxClick.emit(box);
  }

  copyEmail(email: string) {
    navigator.clipboard.writeText(email).then(() => {
      // Inline feedback: swap this row's copy icon to a check briefly instead of a toast.
      this.copiedBox.set(email);
      clearTimeout(this.copiedTimer);
      this.copiedTimer = setTimeout(() => this.copiedBox.set(null), 1500);
    });
  }
}
