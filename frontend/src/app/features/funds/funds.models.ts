import { HttpErrorResponse } from '@angular/common/http';
import { extractErrorMessage } from '../../core/http/problem-details';
import { FundWriteRequest } from '../../data-access/funds/funds-api.models';

export interface FundSaveError {
  message: string;
  /** The debt/loan list is stale (the linked obligation was cancelled or deleted) — reload it. */
  reloadLinks: boolean;
}

/**
 * Readable messages for the debt/loan link checks of POST/PATCH /funds (api-design.md, «Фонды»):
 * 400 — the linked debt/loan does not exist or its currency differs from the fund currency;
 * 409 — a new link to a cancelled debt/loan. Other errors keep the server's own text.
 */
export function fundSaveErrorMessage(error: unknown, request: FundWriteRequest): FundSaveError {
  const fallback = extractErrorMessage(error, 'Не удалось сохранить фонд.');
  const hasLink = !!(request.linkedDebtId || request.linkedLoanId);
  if (!hasLink || !(error instanceof HttpErrorResponse)) {
    return { message: fallback, reloadLinks: false };
  }
  const what = request.linkedDebtId ? 'долг' : 'кредит';
  if (error.status === 409) {
    return {
      message: `Выбранный ${what} уже отменён — связать с ним фонд нельзя. Выберите другую связь или уберите её.`,
      reloadLinks: true,
    };
  }
  if (error.status === 400) {
    const detail = fallback.toLowerCase();
    if (detail.includes('валют')) {
      return {
        message: `Валюта выбранного ${what === 'долг' ? 'долга' : 'кредита'} отличается от валюты фонда. Выберите связь в той же валюте или смените валюту фонда.`,
        reloadLinks: false,
      };
    }
    if (detail.includes('не найден')) {
      return {
        message: `Выбранный ${what} не найден — возможно, он удалён. Список обновлён, выберите связь заново.`,
        reloadLinks: true,
      };
    }
  }
  return { message: fallback, reloadLinks: false };
}
