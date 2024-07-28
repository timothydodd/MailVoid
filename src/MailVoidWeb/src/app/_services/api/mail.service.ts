import { HttpClient } from "@angular/common/http";
import { inject, Injectable } from "@angular/core";

@Injectable({ providedIn: 'root' })
export class MailService {


    private http = inject(HttpClient)
   getMailboxes() {
    
    return this.http.get<string[]>('api/mail/boxes');
   }
   getEmails(options:FilterOptions| undefined) {
    return this.http.post<Mail[]>(`api/mail`,options);
   }
   getEmail(id: string) {
    return this.http.get<Mail>(`api/mail/${id}`);
   }
}
export interface FilterOptions{
  to:string;
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