import { ChangeDetectionStrategy, Component, computed, input, model, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { MailBoxGroups } from '../../../_services/api/mail.service';
import { BoxMenuComponent } from './box-menu/box-menu.component';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [LucideAngularModule, BoxMenuComponent],
  template: `
    <div class="box-list-container">
      @if (sortedMailboxes(); as mb) {
        @for (group of mb; track $index) {
          <div class="group-section">
            <div class="group-header">
              <h3 class="group-title">{{ group.groupName }}</h3>
              <div class="group-indicators">
                @if (group.isOwner) {
                  <lucide-icon name="crown" size="12" class="group-owner-icon" title="You own this group"></lucide-icon>
                }
                @if (group.isPublic) {
                  <lucide-icon name="users" size="12" class="group-public-icon" title="Public group"></lucide-icon>
                } @else {
                  <lucide-icon name="lock" size="12" class="group-private-icon" title="Private group"></lucide-icon>
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
                    @if (item.unreadCount > 0) {
                      <span class="unread-count">{{ item.unreadCount }}</span>
                    }
                  </button>
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

  deleteClick(item: string) {
    console.log('Deleted: ', item);
    this.deleteEvent.emit(item);
  }
}
