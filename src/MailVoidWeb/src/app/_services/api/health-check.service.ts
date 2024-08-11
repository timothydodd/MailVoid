import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

//import { environment } from "@admin-web/env/environment";

@Injectable({ providedIn: 'root' })
export class HealthCheckService {
  constructor(private http: HttpClient) {}
  getHealth() {
    // Parameter to ignore auth token (anonymous is allowed)
    return this.http.get<any>(`${environment.apiUrl}/api/health`);
  }
}
