import { Injectable, inject } from '@angular/core';
import { EMPTY, Observable, expand, forkJoin, map, reduce } from 'rxjs';
import { BalanceHistoryApiService } from '../../data-access/balance-history/balance-history-api.service';
import { Operation } from '../../data-access/operations/operations-api.models';
import { OperationsApiService } from '../../data-access/operations/operations-api.service';
import { Money, subtractAmounts } from '../../shared/money/money';

/** What the "Указать баланс" form shows for a wallet and a date (ADR-0003 §6, §7). */
export interface StatedBalanceContext {
  /** The balance at the end of the date by the operations alone, without the correction amount of that date. */
  operationsBalance: Money;
  /** The balance already stated for the date (`statedBalance` of its correction amount), or null. */
  statedBalance: Money | null;
}

/** Page size for the correction lookup: the server maximum used by the other screens. */
const LOOKUP_PAGE_SIZE = 100;

/**
 * Reads the data for the live hint of the "Указать баланс" form: the balance at the end of a day by the operations
 * and the balance already stated for that day.
 *
 * `GET /wallets/{id}/balance?date=` already includes the correction amount of that date, so the correction is found
 * in the operation list of the day (`isCorrection`) and subtracted. A zero correction amount is hidden in the list
 * (ADR-0003 §6, p.7): it changes nothing in the balance, but its stated balance is not visible either — such a date
 * is treated as "not stated yet" (no prefill).
 */
@Injectable({ providedIn: 'root' })
export class StatedBalanceLookupService {
  private readonly balanceHistory = inject(BalanceHistoryApiService);
  private readonly operations = inject(OperationsApiService);

  load(walletId: string, date: string): Observable<StatedBalanceContext> {
    return forkJoin({
      snapshot: this.balanceHistory.getBalanceOnDate(walletId, date),
      correction: this.findCorrection(walletId, date),
    }).pipe(
      map(({ snapshot, correction }) => ({
        operationsBalance: correction
          ? { amount: subtractAmounts(snapshot.balance.amount, correction.amount.amount), currencyId: snapshot.balance.currencyId }
          : snapshot.balance,
        statedBalance: correction?.statedBalance ?? null,
      })),
    );
  }

  /** The correction amount of the day: it comes last inside its day, so every page of the day is followed. */
  private findCorrection(walletId: string, date: string): Observable<Operation | null> {
    const page = (cursor?: string) =>
      this.operations.list({ walletId, dateFrom: date, dateTo: date, limit: LOOKUP_PAGE_SIZE, cursor });

    return page().pipe(
      expand((result) => (result.pagination.hasMore && result.pagination.nextCursor ? page(result.pagination.nextCursor) : EMPTY)),
      reduce<{ data: Operation[] }, Operation | null>(
        (found, result) => found ?? result.data.find((operation) => operation.isCorrection) ?? null,
        null,
      ),
    );
  }
}
