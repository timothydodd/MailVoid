import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class MobileMenuService {
  private toggleMenuSignal = new Subject<void>();

  toggleMenu() {
    this.toggleMenuSignal.next();
  }

  get menuToggled() {
    return this.toggleMenuSignal.asObservable();
  }
}
