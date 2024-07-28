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
    return this.http.post<Mail[]>(`${environment.apiUrl}/api/mail`, options);
  }
  getEmail(id: string) {
    return this.http.get<Mail>(`${environment.apiUrl}/api/mail/${id}`);
  }
}
export interface FilterOptions {
  to: string;
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
