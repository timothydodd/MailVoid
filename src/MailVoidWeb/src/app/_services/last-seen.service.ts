import { Injectable } from '@angular/core';

export interface LastSeenData {
  [mailGroupPath: string]: string; // ISO date string
}

@Injectable({
  providedIn: 'root',
})
export class LastSeenService {
  private readonly STORAGE_KEY = 'mailvoid_last_seen';

  constructor() {}

  /**
   * Get the user's last seen date for a specific mailgroup
   */
  getLastSeen(mailGroupPath: string): Date | null {
    const data = this.getLastSeenData();
    const lastSeenStr = data[mailGroupPath];
    return lastSeenStr ? new Date(lastSeenStr) : null;
  }

  /**
   * Set the user's last seen date for a specific mailgroup
   */
  setLastSeen(mailGroupPath: string, date: Date = new Date()): void {
    const data = this.getLastSeenData();
    data[mailGroupPath] = date.toISOString();
    this.saveLastSeenData(data);
  }

  /**
   * Check if a mailgroup has activity since the user last saw it
   */
  hasUnseenActivity(mailGroupPath: string, lastActivity: Date | null): boolean {
    if (!lastActivity) return false;

    const lastSeen = this.getLastSeen(mailGroupPath);
    if (!lastSeen) return true; // Never seen = has unseen activity

    return lastActivity > lastSeen;
  }

  /**
   * Get all last seen data for the current user
   */
  private getLastSeenData(): LastSeenData {
    try {
      const stored = localStorage.getItem(this.STORAGE_KEY);
      return stored ? JSON.parse(stored) : {};
    } catch (error) {
      console.error('Error reading last seen data:', error);
      return {};
    }
  }

  /**
   * Save last seen data to localStorage
   */
  private saveLastSeenData(data: LastSeenData): void {
    try {
      localStorage.setItem(this.STORAGE_KEY, JSON.stringify(data));
    } catch (error) {
      console.error('Error saving last seen data:', error);
    }
  }

  /**
   * Clear all last seen data (useful for logout)
   */
  clearLastSeenData(): void {
    try {
      localStorage.removeItem(this.STORAGE_KEY);
    } catch (error) {
      console.error('Error clearing last seen data:', error);
    }
  }
}
