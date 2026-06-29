import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class MailSearchService {
  readonly searchText = signal<string>('');

  setSearch(value: string) {
    this.searchText.set(value);
  }

  clear() {
    this.searchText.set('');
  }
}
