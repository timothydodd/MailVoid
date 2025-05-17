import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, model, output } from '@angular/core';
import { LucideAngularModule } from 'lucide-angular';
import { MailBoxGroups } from '../../../_services/api/mail.service';
import { BoxMenuComponent } from './box-menu/box-menu.component';

@Component({
  selector: 'app-box-list',
  standalone: true,
  imports: [CommonModule, LucideAngularModule, BoxMenuComponent],
  template: `
    @if (mailboxes(); as mb) {
      @for (group of mb; track $index) {
        <div>{{ group.groupName }}</div>
        @for (item of group.mailBoxes; track $index) {
          <div class="list-group-item">
            <button class="btn btn-link" (click)="clickBox(item)">{{ item }}</button>
            <app-box-menu [item]="item" (deleteEvent)="deleteEvent.emit($event)"></app-box-menu>
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
