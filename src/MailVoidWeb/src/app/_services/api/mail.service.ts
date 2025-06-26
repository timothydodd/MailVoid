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
        const groups: MailBoxGroups[] = [];

        // Separate claimed and regular mailboxes
        const claimedBoxes: MailBox[] = [];
        const regularBoxes: MailBox[] = [];

        boxes.forEach((box) => {
          if (box.path?.startsWith('user-')) {
            claimedBoxes.push(box);
          } else {
            regularBoxes.push(box);
          }
        });

        // Add "My Boxes" section if there are claimed mailboxes (always first)
        if (claimedBoxes.length > 0) {
          groups.push({ groupName: 'My Boxes', mailBoxes: claimedBoxes, isOwner: true, isPublic: false });
        }

        // Process regular mailboxes
        regularBoxes.forEach((box) => {
          const groupName = box.mailBoxName ?? box.path ?? 'Ungrouped';
          const group = groups.find((g) => g.groupName === groupName);
          if (group) {
            group.mailBoxes.push(box);
          } else {
            groups.push({
              groupName: groupName,
              mailBoxes: [box],
              isOwner: box.isOwner,
              isPublic: box.isPublic,
            });
          }
        });

        // Sort groups: My Boxes first, then alphabetically, with Ungrouped last
        return groups.sort((a, b) => {
          if (a.groupName === 'My Boxes') return -1;
          if (b.groupName === 'My Boxes') return 1;
          if (a.groupName === 'Ungrouped') return 1;
          if (b.groupName === 'Ungrouped') return -1;
          return a.groupName.localeCompare(b.groupName);
        });
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

  updateMailGroup(mailGroup: Partial<MailGroup> & { id: number }) {
    return this.http.put<MailGroup>(`${environment.apiUrl}/api/mail/groups/${mailGroup.id}`, mailGroup);
  }

  getUsers() {
    return this.http.get<User[]>(`${environment.apiUrl}/api/mail/users`);
  }

  getMailGroupUsers(mailGroupId: number) {
    return this.http.get<MailGroupUser[]>(`${environment.apiUrl}/api/mail/groups/${mailGroupId}/users`);
  }

  grantUserAccess(mailGroupId: number, userId: string) {
    return this.http.post(`${environment.apiUrl}/api/mail/groups/${mailGroupId}/access`, { userId });
  }

  revokeUserAccess(mailGroupId: number, userId: string) {
    return this.http.delete(`${environment.apiUrl}/api/mail/groups/${mailGroupId}/access/${userId}`);
  }

  // Mail Group management
  createMailGroup(data: CreateMailGroupRequest) {
    return this.http.post<MailGroup>(`${environment.apiUrl}/api/mail/mail-groups`, data);
  }

  deleteMailGroup(id: number) {
    return this.http.delete(`${environment.apiUrl}/api/mail/mail-groups/${id}`);
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
  path: string | null;
  subdomain: string | null;
  description: string | null;
  isPublic: boolean;
  isUserPrivate: boolean;
  createdAt: string;
  lastActivity: string | null;
  isOwner: boolean;
}
export interface PagedResults<T> {
  items: T[] | null;
  totalCount: number;
}

export interface MailBox {
  path: string | null;
  name: string;
  mailBoxName: string;
  isOwner: boolean;
  isPublic: boolean;
}
export interface MailBoxGroups {
  groupName: string;
  mailBoxes: MailBox[];
  isOwner: boolean;
  isPublic: boolean;
}
export interface GrantAccessRequest {
  userId: string;
}

export interface CreateMailGroupRequest {
  subdomain: string;
  description?: string;
  isPublic: boolean;
}

export interface User {
  id: string;
  userName: string;
  role: number;
  timeStamp: string;
}

export interface MailGroupUser {
  id: number;
  mailGroupId: number;
  userId: string;
  grantedAt: string;
  user: User;
}
