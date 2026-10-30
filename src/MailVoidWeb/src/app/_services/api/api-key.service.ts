import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

/** Personal API keys for the read-only /api/v1 endpoints (managed with the session token). */
@Injectable({ providedIn: 'root' })
export class ApiKeyService {
  private http = inject(HttpClient);
  private baseUrl = `${environment.apiUrl}/api/apikeys`;

  getKeys() {
    return this.http.get<ApiKey[]>(this.baseUrl);
  }

  /** The response is the only time the plaintext `key` is available. */
  createKey(name: string) {
    return this.http.post<CreatedApiKey>(this.baseUrl, { name });
  }

  deleteKey(id: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}

export interface ApiKey {
  id: number;
  name: string;
  /** First characters of the key, for telling keys apart. */
  prefix: string;
  createdAt: string;
  lastUsedAt: string | null;
}

export interface CreatedApiKey extends ApiKey {
  key: string;
}
