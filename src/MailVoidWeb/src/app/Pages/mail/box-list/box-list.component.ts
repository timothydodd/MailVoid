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
                    (claimEvent)="claimEvent.emit($event)"
                    (unclaimEvent)="unclaimEvent.emit($event)">
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
  claimEvent = output<string>();
  unclaimEvent = output<string>();
  
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
  
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  
  deleteClick(item: string) {
    console.log('Deleted: ', item);
    this.deleteEvent.emit(item);
  }
}
