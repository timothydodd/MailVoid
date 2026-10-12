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

// Backoff for our own restart loop, used once withAutomaticReconnect() has given up
// (or the initial start failed). Doubles from 2s up to a 60s cap.
const RESTART_BASE_DELAY_MS = 2_000;
const RESTART_MAX_DELAY_MS = 60_000;

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

  // True between startConnection() and stopConnection(): the user is logged in and
  // wants a live connection, so the restart loop should keep trying.
  private shouldRun = false;
  private restartTimer?: ReturnType<typeof setTimeout>;
  private restartAttempt = 0;

  constructor(private authService: AuthService) {}

  public async startConnection(): Promise<void> {
    if (!this.authService.getToken()) {
      return;
    }
    this.shouldRun = true;

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

    if (!this.hubConnection) {
      this.buildConnection();
    }

    this.clearRestartTimer();
    this.connectionState.set('connecting');
    this.startPromise = this.hubConnection!.start()
      .then(() => {
        this.restartAttempt = 0;
        this.connectionState.set('connected');
      })
      .catch((err) => {
        console.error('Error starting SignalR connection:', err);
        this.connectionState.set('disconnected');
        this.scheduleRestart();
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
        // Resolved on every (re)connect. Refreshes first (through AuthService's shared
        // single-flight refresh) when the stored access token is expired or about to be.
        accessTokenFactory: async () => (await this.authService.getFreshToken()) ?? '',
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
      this.restartAttempt = 0;
      this.connectionState.set('connected');
    });

    // Fires after withAutomaticReconnect() exhausts its retries (or on stop()).
    // Keep trying with our own backoff while the user is still logged in.
    this.hubConnection.onclose(() => {
      this.connectionState.set('disconnected');
      this.scheduleRestart();
    });
  }

  private scheduleRestart(): void {
    if (!this.shouldRun || this.restartTimer) return;
    if (!this.authService.isLoggedIn.value || !this.authService.getToken()) {
      this.shouldRun = false;
      return;
    }
    const delay = Math.min(RESTART_BASE_DELAY_MS * 2 ** this.restartAttempt, RESTART_MAX_DELAY_MS);
    this.restartAttempt++;
    this.restartTimer = setTimeout(() => {
      this.restartTimer = undefined;
      if (!this.shouldRun) return;
      // Failures are logged and rescheduled inside startConnection().
      this.startConnection().catch(() => undefined);
    }, delay);
  }

  private clearRestartTimer(): void {
    if (this.restartTimer) {
      clearTimeout(this.restartTimer);
      this.restartTimer = undefined;
    }
  }

  public async stopConnection(): Promise<void> {
    // Stop the restart loop first so the onclose triggered by stop() doesn't re-arm it.
    this.shouldRun = false;
    this.restartAttempt = 0;
    this.clearRestartTimer();
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
