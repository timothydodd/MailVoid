import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class WebhookService {
  private http = inject(HttpClient);

  getBuckets() {
    return this.http.get<WebhookBucket[]>(`${environment.apiUrl}/api/webhooks/buckets`);
  }

  getBucket(name: string) {
    return this.http.get<WebhookBucket>(`${environment.apiUrl}/api/webhooks/buckets/${name}`);
  }

  getWebhooks(bucketName: string, page: number = 1, pageSize: number = 50) {
    return this.http.get<PagedWebhooks>(`${environment.apiUrl}/api/webhooks/buckets/${bucketName}/webhooks`, {
      params: { page: page.toString(), pageSize: pageSize.toString() },
    });
  }

  getWebhook(id: number) {
    return this.http.get<WebhookDetail>(`${environment.apiUrl}/api/webhooks/${id}`);
  }

  deleteWebhook(id: number) {
    return this.http.delete(`${environment.apiUrl}/api/webhooks/${id}`);
  }

  deleteBucket(name: string) {
    return this.http.delete(`${environment.apiUrl}/api/webhooks/buckets/${name}`);
  }

  /** Deletes every captured webhook in a bucket; the bucket itself is kept. */
  purgeBucket(name: string) {
    return this.http.delete<PurgeResult>(`${environment.apiUrl}/api/webhooks/buckets/${name}/webhooks`);
  }

  /** Deletes every captured webhook in every bucket the user can access. */
  purgeAll() {
    return this.http.delete<PurgeResult>(`${environment.apiUrl}/api/webhooks`);
  }
}

export interface WebhookBucket {
  id: number;
  name: string;
  description: string | null;
  isPublic: boolean;
  createdAt: string;
  lastActivity: string | null;
  retentionDays: number | null;
}

export interface WebhookListItem {
  id: number;
  bucketName: string;
  httpMethod: string;
  path: string;
  queryString: string | null;
  contentType: string | null;
  sourceIp: string | null;
  createdOn: string;
}

export interface WebhookDetail {
  id: number;
  bucketName: string;
  httpMethod: string;
  path: string;
  queryString: string | null;
  headers: string;
  body: string;
  contentType: string | null;
  sourceIp: string | null;
  createdOn: string;
}

export interface PurgeResult {
  deleted: number;
}

export interface PagedWebhooks {
  items: WebhookListItem[] | null;
  totalCount: number;
  page: number;
  pageSize: number;
}
