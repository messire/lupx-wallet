import { Directive, input } from '@angular/core';

/**
 * One choice of `app-select`, declared in the template the same way an `<option>` was:
 *
 * ```html
 * <app-select formControlName="walletId" placeholder="— выберите кошелек —">
 *   @for (wallet of wallets(); track wallet.id) {
 *     <app-select-option [value]="wallet.id" [label]="wallet.name" />
 *   }
 * </app-select>
 * ```
 *
 * A data carrier only: it renders nothing, `app-select` reads the options through `contentChildren` and
 * draws its own list.
 */
@Directive({
  selector: 'app-select-option',
  standalone: true,
  host: { hidden: '' },
})
export class SelectOptionDirective {
  readonly value = input.required<string>();
  readonly label = input.required<string>();
  readonly disabled = input(false);
}
