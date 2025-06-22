import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class MailService {
  private http = inject(HttpClient);
  getMailboxes() {
    return this.http.get<MailBox[]>(`${environment.apiUrl}/api/mail/boxes`).pipe(
      map((boxes) => {
        const groups: MailBoxGroups[] = [{ groupName: '', mailBoxes: [] }];
        
        // Separate claimed and regular mailboxes
        const claimedBoxes: string[] = [];
        const regularBoxes: MailBox[] = [];
        
        boxes.forEach((box) => {
          if (box.path?.startsWith('user-')) {
            claimedBoxes.push(box.name);
          } else {
            regularBoxes.push(box);
          }
        });
        
        // Add "My Boxes" section if there are claimed mailboxes
        if (claimedBoxes.length > 0) {
          groups.push({ groupName: 'My Boxes', mailBoxes: claimedBoxes });
        }
        
        // Process regular mailboxes
        regularBoxes.forEach((box) => {
          const groupName = box.path ?? '';
          const mailBoxName = box.name;
          const group = groups.find((g) => g.groupName === groupName);
          if (group) {
            group.mailBoxes.push(mailBoxName);
          } else {
            groups.push({ groupName: groupName, mailBoxes: [mailBoxName] });
          }
        });
        
        return groups;
      })
    );
  }
  getEmails(options: FilterOptions | undefined) {
    return this.http.post<PagedResults<Mail>>(`${environment.apiUrl}/api/mail`, options);
  }
  getEmail(id: string) {
    return this.http.get<Mail>(`${environment.apiUrl}/api/mail/${id}`);
  }
  deleteBoxes(options: FilterOptions | undefined) {
    return this.http.delete(`${environment.apiUrl}/api/mail/boxes`, { body: options });
  }
  getMailGroups() {
    return this.http.get<MailGroup[]>(`${environment.apiUrl}/api/mail/groups`);
  }
  saveMailGroup(mailGroup: MailGroup) {
    return this.http.post<MailGroup>(`${environment.apiUrl}/api/mail/groups`, mailGroup);
  }
  
  // Claimed Mailbox methods
  getMyClaimedMailboxes() {
    return this.http.get<ClaimedMailbox[]>(`${environment.apiUrl}/api/claimedmailbox/my-mailboxes`);
  }
  
  getUnclaimedEmailAddresses() {
    return this.http.get<string[]>(`${environment.apiUrl}/api/claimedmailbox/unclaimed`);
  }
  
  claimMailbox(emailAddress: string) {
    return this.http.post<ClaimedMailbox>(`${environment.apiUrl}/api/claimedmailbox/claim`, { emailAddress });
  }
  
  unclaimMailbox(emailAddress: string) {
    return this.http.delete(`${environment.apiUrl}/api/claimedmailbox/unclaim`, { body: { emailAddress } });
  }
  
  isEmailClaimed(emailAddress: string) {
    return this.http.get<boolean>(`${environment.apiUrl}/api/claimedmailbox/check/${encodeURIComponent(emailAddress)}`);
  }
}
export interface FilterOptions {
  to: string | null;
}
export interface Mail {
  id: number;
  to: string;
  text: string;
  isHtml: boolean;
  from: string;
  subject: string;
  charsets: string | null;
  createdOn: string;
}

export interface MailGroup {
  id: number;
  path: string;
  rules: string | null;
  ownerUserId: string;
  isPublic: boolean;
}
export interface PagedResults<T> {
  items: T[] | null;
  totalCount: number;
}

export interface MailBox {
  path: string | null;
  name: string;
}
export interface MailBoxGroups {
  groupName: string;
  mailBoxes: string[];
}
export interface MailRule {
  value: string;
  typeId: number;
}

export interface ClaimedMailbox {
  id: number;
  emailAddress: string;
  claimedOn: string;
  isActive: boolean;
}
