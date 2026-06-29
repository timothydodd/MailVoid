import { ChangeDetectionStrategy, Component, computed, inject, input, model, output } from '@angular/core';
import { LucideDynamicIcon } from '@lucide/angular';
import { ToastService } from '@rd-ui';
import { MailBoxGroups } from '../../../_services/api/mail.service';
import { BoxMenuComponent } from './box-menu/box-menu.component';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [LucideDynamicIcon, BoxMenuComponent],
  template: `
    <div class="box-list-container">
      @if (sortedMailboxes(); as mb) {
        @for (group of mb; track $index) {
          <div class="group-section">
            <div class="group-header">
              <h3 class="group-title">{{ group.groupName }}</h3>
              <div class="group-indicators">
                @if (group.isOwner) {
                  <svg lucideIcon="crown" size="12" class="group-owner-icon" title="You own this group"></svg>
                }
                @if (group.isPublic) {
                  <svg lucideIcon="users" size="12" class="group-public-icon" title="Public group"></svg>
                } @else {
                  <svg lucideIcon="lock" size="12" class="group-private-icon" title="Private group"></svg>
                }
              </div>
            </div>
            <div class="mailbox-list">
              @for (item of group.mailBoxes; track $index) {
                <div class="mailbox-item" [class.selected]="selectedBox() === item.name">
                  <button class="mailbox-button" (click)="clickBox(item.name)" [title]="item.name">
                    <span class="mailbox-name">
                      <span class="email-address">{{ item.name }}</span>
                      @if (item.mailBoxName && group.groupName === 'My Boxes') {
                        <span class="mailbox-subdomain">{{ item.mailBoxName }}</span>
                      }
                    </span>
                  </button>
                  <button class="copy-btn" (click)="copyEmail(item.name)" title="Copy email address">
                    <svg lucideIcon="copy" size="14"></svg>
                  </button>
                  <span class="row-spacer"></span>
                  @if (item.unreadCount > 0) {
                    <span class="unread-count">{{ item.unreadCount }}</span>
                  }
                  <app-box-menu
                    [item]="item.name"
                    [groupName]="group.groupName"
                    [unreadCount]="item.unreadCount"
                    (deleteEvent)="deleteEvent.emit($event)"
                    (markAllAsReadEvent)="markAllAsReadEvent.emit($event)"
                  >
                  </app-box-menu>
                </div>
              }
            </div>
          </div>
        }
      } @else {
        <div class="empty-state">
          <p>No mailboxes available</p>
        </div>
      }
    </div>
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

  private toastr = inject(ToastService);

  sortedMailboxes = computed(() => {
    const mb = this.mailboxes();
    if (!mb) return null;

    // Sorting is now handled in the service, just return as-is
    return mb;
  });

  clickBox(box: string | null) {
    this.selectedBox.set(box);
    this.boxClick.emit(box);
  }

  copyEmail(email: string) {
    navigator.clipboard.writeText(email).then(() => {
      this.toastr.success('Email address copied');
    });
  }

  deleteClick(item: string) {
    console.log('Deleted: ', item);
    this.deleteEvent.emit(item);
  }
}
