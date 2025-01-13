import { CommonModule } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  ViewEncapsulation,
  booleanAttribute,
  forwardRef,
  input,
  model,
  numberAttribute,
  output,
  viewChild,
} from '@angular/core';
import { NG_VALUE_ACCESSOR } from '@angular/forms';

export const INPUTSWITCH_VALUE_ACCESSOR: any = {
  provide: NG_VALUE_ACCESSOR,
  useExisting: forwardRef(() => InputSwitchComponent),
  multi: true,
};
/**
 * InputSwitch is used to select a boolean value.
 * @group Components
 */
@Component({
    selector: 'app-switch',
    imports: [CommonModule],
    template: `
    <button
      [ngClass]="{
        'p-input-switch p-component': true,
        'p-input-switch-checked': checked(),
        'p-disabled': disabled(),
        'p-focus': focused,
      }"
      [ngStyle]="style()"
      [class]="styleClass() ?? ''"
      (click)="onClick($event)"
      [attr.data-pc-name]="'input-switch'"
      [attr.data-pc-section]="'root'"
    >
      <div
        class="p-hidden-accessible"
        [attr.data-pc-section]="'hiddenInputWrapper'"
        [attr.data-p-hidden-accessible]="true"
      >
        <input
          #input
          [attr.id]="inputId()"
          type="checkbox"
          role="switch"
          [checked]="checked()"
          [disabled]="disabled()"
          [attr.aria-checked]="checked()"
          [attr.aria-labelledby]="ariaLabelledBy()"
          [attr.aria-label]="ariaLabel()"
          [attr.name]="name()"
          [attr.tabindex]="tabindex()"
          (focus)="onFocus()"
          (blur)="onBlur()"
          [attr.data-pc-section]="'hiddenInput'"
          pAutoFocus
        />
      </div>
      <span class="p-input-switch-slider" [attr.data-pc-section]="'slider'"></span>
    </button>
  `,
    providers: [INPUTSWITCH_VALUE_ACCESSOR],
    changeDetection: ChangeDetectionStrategy.OnPush,
    encapsulation: ViewEncapsulation.None,
    styleUrls: ['./input-switch.scss'],
    host: {
        class: 'p-element',
    }
})
export class InputSwitchComponent {
  /**
   * Inline style of the component.
   * @group Props
   */
  style = input<{
    [klass: string]: any;
  } | null>();
  /**
   * Style class of the component.
   * @group Props
   */
  styleClass = input<string>();
  /**
   * Index of the element in tabbing order.
   * @group Props
   */
  tabindex = input<number, number | string>(numberAttribute(undefined), { transform: numberAttribute });
  /**
   * Identifier of the input element.
   * @group Props
   */
  inputId = input<string>();
  /**
   * Name of the input element.
   * @group Props
   */
  name = input<string>();
  /**
   * When present, it specifies that the element should be disabled.
   * @group Props
   */
  disabled = model<boolean>(false);
  /**
   * When present, it specifies that the component cannot be edited.
   * @group Props
   */
  readonly = input<boolean>(false);
  /**
   * Value in checked state.
   * @group Props
   */
  trueValue = input<any>(true);
  /**
   * Value in unchecked state.
   * @group Props
   */
  falseValue = input<any>(false);
  /**
   * Used to define a string that autocomplete attribute the current element.
   * @group Props
   */
  ariaLabel = input<string>();
  /**
   * Establishes relationships between the component and label(s) where its value should be one or more element IDs.
   * @group Props
   */
  ariaLabelledBy = input<string>();
  /**
   * When present, it specifies that the component should automatically get focus on load.
   * @group Props
   */
  autofocus = input<boolean, boolean | string>(booleanAttribute(undefined), { transform: booleanAttribute });
  /**
   * Callback to invoke when the on value change.
   * @param {InputSwitchChangeEvent} event - Custom change event.
   * @group Emits
   */
  onChange = output<InputSwitchChangeEvent>();

  input = viewChild<ElementRef>('input');

  modelValue: any = false;

  focused: boolean = false;

  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  onModelChange: (v: any) => void = () => {};

  // eslint-disable-next-line @typescript-eslint/no-unsafe-function-type
  onModelTouched: (v: any) => void = () => {};

  onClick(event: Event) {
    if (!this.disabled() && !this.readonly()) {
      this.modelValue = this.checked() ? this.falseValue() : this.trueValue();

      this.onModelChange(this.modelValue);
      this.onChange.emit({
        originalEvent: event,
        checked: this.modelValue,
      });

      this.input()?.nativeElement.focus();
    }
  }

  onFocus() {
    this.focused = true;
  }

  onBlur() {
    this.focused = false;
    this.onModelTouched(null);
  }

  writeValue(value: any): void {
    this.modelValue = value;
  }

  registerOnChange(fn: () => void): void {
    this.onModelChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onModelTouched = fn;
  }

  setDisabledState(val: boolean): void {
    this.disabled.set(val);
  }

  checked() {
    return this.modelValue === this.trueValue();
  }
}

export interface InputSwitchChangeEvent {
  /**
   * Browser event.
   */
  originalEvent: Event;
  /**
   * Checked state.
   */
  checked: boolean;
}
