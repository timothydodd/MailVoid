import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ClaimedMailbox, MailService } from '../../_services/api/mail.service';
import { finalize } from 'rxjs';

@Component({
  selector: 'app-mailbox-claim',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="mailbox-claim-container">

      <!-- My Claimed Mailboxes Section -->
      <div class="section">
        <div class="section-header">
          <h4 class="section-title">My Boxes</h4>
          <span class="count-badge">{{ myMailboxes().length }}</span>
        </div>
        
        @if (myMailboxes().length === 0) {
          <div class="empty-state">
            <p class="empty-message">You haven't claimed any mailboxes yet</p>
            <p class="empty-hint">Claim an email address below to start receiving emails</p>
          </div>
        } @else {
          <div class="mailbox-list">
            @for (mailbox of myMailboxes(); track mailbox.id) {
              <div class="mailbox-item claimed">
                <div class="mailbox-info">
                  <span class="email-address">{{ mailbox.emailAddress }}</span>
                  <span class="claimed-date">Claimed {{ formatDate(mailbox.claimedOn) }}</span>
                </div>
                <button 
                  class="btn btn-danger btn-sm"
                  [disabled]="isLoading()"
                  (click)="unclaimMailbox(mailbox.emailAddress)"
                >
                  Release
                </button>
              </div>
            }
          </div>
        }
      </div>

      <!-- Available Mailboxes Section -->
      <div class="section">
        <div class="section-header">
          <h4 class="section-title">Available Mailboxes</h4>
          <button 
            class="btn btn-secondary btn-sm"
            [disabled]="isLoading()"
            (click)="refreshUnclaimedEmails()"
          >
            <span [class.loading]="isLoading()">Refresh</span>
          </button>
        </div>
        
        @if (unclaimedEmails().length === 0) {
          <div class="empty-state">
            <p class="empty-message">No unclaimed mailboxes available</p>
            <p class="empty-hint">New email addresses will appear here when emails are received</p>
          </div>
        } @else {
          <div class="mailbox-list">
            @for (email of unclaimedEmails(); track email) {
              <div class="mailbox-item available">
                <div class="mailbox-info">
                  <span class="email-address">{{ email }}</span>
                  <span class="status">Available</span>
                </div>
                <button 
                  class="btn btn-primary btn-sm"
                  [disabled]="isLoading()"
                  (click)="claimMailbox(email)"
                >
                  Claim
                </button>
              </div>
            }
          </div>
        }
      </div>

      @if (errorMessage()) {
        <div class="alert alert-error">
          {{ errorMessage() }}
        </div>
      }

      @if (successMessage()) {
        <div class="alert alert-success">
          {{ successMessage() }}
        </div>
      }
    </div>
  `,
  styleUrl: './mailbox-claim.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class MailboxClaimComponent {
  private mailService = inject(MailService);
  
  myMailboxes = signal<ClaimedMailbox[]>([]);
  unclaimedEmails = signal<string[]>([]);
  isLoading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  constructor() {
    this.loadData();
  }

  private loadData() {
    this.isLoading.set(true);
    this.clearMessages();

    // Load both claimed and unclaimed mailboxes
    Promise.all([
      this.mailService.getMyClaimedMailboxes().toPromise(),
      this.mailService.getUnclaimedEmailAddresses().toPromise()
    ]).then(([claimed, unclaimed]) => {
      this.myMailboxes.set(claimed || []);
      this.unclaimedEmails.set(unclaimed || []);
    }).catch(error => {
      console.error('Error loading mailbox data:', error);
      this.errorMessage.set('Failed to load mailbox data');
    }).finally(() => {
      this.isLoading.set(false);
    });
  }

  claimMailbox(emailAddress: string) {
    this.isLoading.set(true);
    this.clearMessages();

    this.mailService.claimMailbox(emailAddress)
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (claimedMailbox) => {
          this.successMessage.set(`Successfully claimed ${emailAddress}`);
          // Update local state
          this.myMailboxes.update(boxes => [...boxes, claimedMailbox]);
          this.unclaimedEmails.update(emails => emails.filter(e => e !== emailAddress));
          // Clear success message after 3 seconds
          setTimeout(() => this.successMessage.set(''), 3000);
        },
        error: (error) => {
          console.error('Error claiming mailbox:', error);
          this.errorMessage.set(error.error?.message || 'Failed to claim mailbox');
          setTimeout(() => this.errorMessage.set(''), 5000);
        }
      });
  }

  unclaimMailbox(emailAddress: string) {
    this.isLoading.set(true);
    this.clearMessages();

    this.mailService.unclaimMailbox(emailAddress)
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: () => {
          this.successMessage.set(`Successfully released ${emailAddress}`);
          // Update local state
          this.myMailboxes.update(boxes => boxes.filter(b => b.emailAddress !== emailAddress));
          this.unclaimedEmails.update(emails => [...emails, emailAddress].sort());
          // Clear success message after 3 seconds
          setTimeout(() => this.successMessage.set(''), 3000);
        },
        error: (error) => {
          console.error('Error unclaiming mailbox:', error);
          this.errorMessage.set('Failed to release mailbox');
          setTimeout(() => this.errorMessage.set(''), 5000);
        }
      });
  }

  refreshUnclaimedEmails() {
    this.isLoading.set(true);
    this.clearMessages();

    this.mailService.getUnclaimedEmailAddresses()
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (emails) => {
          this.unclaimedEmails.set(emails);
          this.successMessage.set('Refreshed available mailboxes');
          setTimeout(() => this.successMessage.set(''), 2000);
        },
        error: (error) => {
          console.error('Error refreshing unclaimed emails:', error);
          this.errorMessage.set('Failed to refresh available mailboxes');
          setTimeout(() => this.errorMessage.set(''), 5000);
        }
      });
  }

  private clearMessages() {
    this.errorMessage.set('');
    this.successMessage.set('');
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    return date.toLocaleDateString('en-US', { 
      year: 'numeric', 
      month: 'short', 
      day: 'numeric' 
    });
  }
}