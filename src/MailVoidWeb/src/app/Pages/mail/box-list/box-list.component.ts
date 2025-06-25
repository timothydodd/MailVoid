import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input, model, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { MailBoxGroups } from '../../../_services/api/mail.service';
import { BoxMenuComponent } from './box-menu/box-menu.component';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, BoxMenuComponent],
  template: `
    <div class="box-list-container">
      <!-- Show All option -->
      <div class="group-section">
        <div class="mailbox-list">
          <div class="mailbox-item show-all-item" [class.selected]="selectedBox() === null">
            <button class="mailbox-button" (click)="clickBox(null)" title="Show all emails">
              <span class="mailbox-name">
                <lucide-icon name="inbox" size="16"></lucide-icon>
                Show All
              </span>
            </button>
          </div>
        </div>
      </div>
      
      @if (sortedMailboxes(); as mb) {
        @for (group of mb; track $index) {
          <div class="group-section">
            <div class="group-header">
              <h3 class="group-title">{{ group.groupName }}</h3>
              <span class="group-count">{{ group.mailBoxes.length }}</span>
            </div>
            <div class="mailbox-list">
              @for (item of group.mailBoxes; track $index) {
                <div class="mailbox-item" [class.selected]="selectedBox() === item">
                  <button class="mailbox-button" (click)="clickBox(item)" [title]="item">
                    <span class="mailbox-name">{{ item }}</span>
                  </button>
                  <app-box-menu
                    [item]="item"
                    [groupName]="group.groupName"
                    (deleteEvent)="deleteEvent.emit($event)"
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
  boxClick = output<string | null>();

  sortedMailboxes = computed(() => {
    const mb = this.mailboxes();
    if (!mb) return null;

    return [...mb].sort((a, b) => {
      // Put ungrouped items (empty or default group names) at the bottom
      const aIsUngrouped = !a.groupName || a.groupName === 'Ungrouped' || a.groupName === '';
      const bIsUngrouped = !b.groupName || b.groupName === 'Ungrouped' || b.groupName === '';

      if (aIsUngrouped && !bIsUngrouped) return 1;
      if (!aIsUngrouped && bIsUngrouped) return -1;

      // Both are grouped or both are ungrouped, sort alphabetically
      return a.groupName.localeCompare(b.groupName);
    });
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
