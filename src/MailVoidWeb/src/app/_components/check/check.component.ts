import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, EventEmitter, forwardRef, Input, input, Output } from '@angular/core';
import { ControlValueAccessor, FormsModule, NG_VALUE_ACCESSOR, ReactiveFormsModule } from '@angular/forms';

const CUSTOM_VALUE_ACCESSOR: any = {
  provide: NG_VALUE_ACCESSOR,
  useExisting: forwardRef(() => CheckComponent),
  multi: true,
};

@Component({
  selector: 'app-check',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule],
  providers: [CUSTOM_VALUE_ACCESSOR],
  template: `
    <div class="checkbox-wrapper-33">
      <label class="checkbox">
        <input
          class="checkbox__trigger visuallyhidden"
          type="checkbox"
          [disabled]="disabled()"
          [(ngModel)]="checked"
          (change)="change($event)"
        /><span class="checkbox__symbol">
          <svg
            aria-hidden="true"
            class="icon-checkbox"
            width="28px"
            height="28px"
            viewBox="0 0 28 28"
            version="1"
            xmlns="http://www.w3.org/2000/svg"
          >
            <path d="M4 14l8 7L24 7"></path>
          </svg>
        </span>
        <p class="checkbox__textwrapper">{{ label() }}</p></label
      >
    </div>
  `,
  styleUrl: './check.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CheckComponent implements ControlValueAccessor {
  constructor() {
    this.onChange = (_: any) => {};
    this.onTouched = () => {};
  }

  @Input()
  checked = false;
  disabled = input(false);
  label = input('Checkbox');
  @Output()
  checkedEvent = new EventEmitter<boolean>();

  private onChange: Function;
  private onTouched: Function;

  writeValue(obj: any): void {
    this.checked = obj;
  }

  registerOnChange(fn: any): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: any): void {
    this.onTouched = fn;
  }
  change(e: any) {
    this.onChange(this.checked);
    this.checkedEvent.emit(this.checked);
    this.onTouched();
  }
}
