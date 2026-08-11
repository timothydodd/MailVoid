import { Injectable, signal } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from '../_services/auth-service';

export interface MailNotification {
  id: number;
  from: string;
  to: string;
  subject: string;
  receivedDate: Date;
  mailGroupPath: string;
}

export interface WebhookNotification {
  id: number;
  bucketName: string;
  httpMethod: string;
  path: string;
  contentType: string;
  createdOn: Date;
}

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

@Injectable({
  providedIn: 'root',
})
export class SignalRService {
  private hubConnection?: signalR.HubConnection;
  private startPromise?: Promise<void>;
  private newMailSubject = new Subject<MailNotification>();
  private newWebhookSubject = new Subject<WebhookNotification>();
  public newMail$ = this.newMailSubject.asObservable();
  public newWebhook$ = this.newWebhookSubject.asObservable();
  public connectionState = signal<ConnectionState>('disconnected');

  constructor(private authService: AuthService) {}

  public async startConnection(): Promise<void> {
    const state = this.hubConnection?.state;
    // Already connected or in the middle of (re)connecting - don't spin up a duplicate.
    if (
      state === signalR.HubConnectionState.Connected ||
      state === signalR.HubConnectionState.Connecting ||
      state === signalR.HubConnectionState.Reconnecting
    ) {
      return;
    }
    if (this.startPromise) {
      return this.startPromise;
    }

    if (!this.authService.getToken()) {
      return;
    }

    if (!this.hubConnection) {
      this.buildConnection();
    }

    this.connectionState.set('connecting');
    this.startPromise = this.hubConnection!.start()
      .then(() => {
        this.connectionState.set('connected');
      })
      .catch((err) => {
        console.error('Error starting SignalR connection:', err);
        this.connectionState.set('disconnected');
        throw err;
      })
      .finally(() => {
        this.startPromise = undefined;
      });

    return this.startPromise;
  }

  private buildConnection(): void {
    const baseUrl = environment.apiUrl || '';
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/mail`, {
        // Resolve the token lazily so reconnects always use a fresh access token.
        accessTokenFactory: () => this.authService.getToken() ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.hubConnection.on('NewMail', (mail: MailNotification) => {
      this.newMailSubject.next(mail);
    });

    this.hubConnection.on('NewWebhook', (webhook: WebhookNotification) => {
      this.newWebhookSubject.next(webhook);
    });

    this.hubConnection.onreconnecting(() => {
      this.connectionState.set('reconnecting');
    });

    this.hubConnection.onreconnected(() => {
      this.connectionState.set('connected');
    });

    this.hubConnection.onclose(() => {
      this.connectionState.set('disconnected');
    });
  }

  public async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      try {
        await this.hubConnection.stop();
      } catch (err) {
        console.error('Error stopping SignalR connection:', err);
      }
      this.connectionState.set('disconnected');
    }
  }

  public isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}
