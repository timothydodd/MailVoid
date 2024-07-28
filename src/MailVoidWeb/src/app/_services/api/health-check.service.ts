import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';
import { InterceptorHttpParams } from '../interceptor.service';

//import { environment } from "@admin-web/env/environment";

@Injectable({ providedIn: 'root' })
export class HealthCheckService {
  constructor(private http: HttpClient) {}
  getHealth() {
    // Parameter to ignore auth token (anonymous is allowed)
    const params = new InterceptorHttpParams({ ignoreAdmin: false, noToken: true });
    return this.http.get<any>(`${environment.apiUrl}/health`, { params: params });
  }
}
