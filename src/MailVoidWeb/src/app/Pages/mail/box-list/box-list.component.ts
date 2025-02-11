import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { NgbModule } from '@ng-bootstrap/ng-bootstrap';
import { LucideAngularModule } from 'lucide-angular';
import { MailBoxGroups } from '../../../_services/api/mail.service';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, NgbModule],
  template: `
    @if (mailboxes(); as mb) {
      @for (group of mb; track $index) {
        <div>{{ group.groupName }}</div>
        @for (item of group.mailBoxes; track $index) {
          <div class="list-group-item">
            <button class="btn btn-link" (click)="clickBox(item)">{{ item }}</button>
            <div ngbDropdown class="borderless-menu" placement="bottom-right" hover-stop-propagation>
              <button class="btn btn-outline-primary" ngbDropdownToggle click-stop-propagation>
                <lucide-icon name="ellipsis-vertical"></lucide-icon>
              </button>
              <div ngbDropdownMenu>
                <button ngbDropdownItem click-stop-propagation (click)="deleteClick(item)">Delete</button>
              </div>
            </div>
          </div>
        }
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
  clickBox(box: string) {
    this.selectedBox.set(box);
  }
  deleteClick(item: string) {
    console.log('Deleted: ', item);
    this.deleteEvent.emit(item);
  }
}
