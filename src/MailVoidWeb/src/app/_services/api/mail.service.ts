import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class MailService {
  private http = inject(HttpClient);
  getMailboxes() {
    return this.http.get<string[]>(`${environment.apiUrl}/api/mail/boxes`);
  }
  getEmails(options: FilterOptions | undefined) {
    if (!options) options = { to: null };
    return this.http.post<Mail[]>(`${environment.apiUrl}/api/mail`, options);
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
