import { Injectable } from '@angular/core';
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

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection?: signalR.HubConnection;
  private newMailSubject = new Subject<MailNotification>();
  public newMail$ = this.newMailSubject.asObservable();

  constructor(private authService: AuthService) {}

  public async startConnection(): Promise<void> {
    if (this.hubConnection && this.hubConnection.state === signalR.HubConnectionState.Connected) {
      return;
    }

    const token = this.authService.getToken();
    if (!token) {
      console.log('No access token available, skipping SignalR connection');
      return;
    }

    const baseUrl = environment.apiUrl || '';
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(`${baseUrl}/hubs/mail`, {
        accessTokenFactory: () => token
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.hubConnection.on('NewMail', (mail: MailNotification) => {
      console.log('New mail received:', mail);
      this.newMailSubject.next(mail);
    });

    this.hubConnection.onreconnecting(error => {
      console.log('SignalR reconnecting...', error);
    });

    this.hubConnection.onreconnected(connectionId => {
      console.log('SignalR reconnected:', connectionId);
    });

    this.hubConnection.onclose(error => {
      console.log('SignalR connection closed:', error);
    });

    try {
      await this.hubConnection.start();
      console.log('SignalR connection started');
    } catch (err) {
      console.error('Error starting SignalR connection:', err);
      setTimeout(() => this.startConnection(), 5000);
    }
  }

  public async stopConnection(): Promise<void> {
    if (this.hubConnection) {
      try {
        await this.hubConnection.stop();
        console.log('SignalR connection stopped');
      } catch (err) {
        console.error('Error stopping SignalR connection:', err);
      }
    }
  }

  public isConnected(): boolean {
    return this.hubConnection?.state === signalR.HubConnectionState.Connected;
  }
}