import { HttpErrorResponse } from '@angular/common/http';
import { fundSaveErrorMessage } from './funds.models';

const base = { name: 'Отпуск', targetAmount: '100.00', currencyId: 'c-rub' };

function problem(status: number, detail: string): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: { status, detail } });
}

describe('fundSaveErrorMessage', () => {
  it('maps 409 on a debt link to a cancelled-debt message and asks to reload the list', () => {
    const result = fundSaveErrorMessage(problem(409, 'Нельзя связать фонд с отменённым долгом или кредитом.'), { ...base, linkedDebtId: 'd-1' });
    expect(result.message).toContain('Выбранный долг уже отменён');
    expect(result.reloadLinks).toBe(true);
  });

  it('maps 400 currency mismatch on a loan link', () => {
    const result = fundSaveErrorMessage(
      problem(400, 'Валюта связанного долга или кредита должна совпадать с валютой фонда.'),
      { ...base, linkedLoanId: 'l-1' },
    );
    expect(result.message).toContain('Валюта выбранного кредита отличается от валюты фонда');
    expect(result.reloadLinks).toBe(false);
  });

  it('maps 400 not-found on a link and asks to reload the list', () => {
    const result = fundSaveErrorMessage(problem(400, 'Связанный кредит не найден.'), { ...base, linkedLoanId: 'l-1' });
    expect(result.message).toContain('Выбранный кредит не найден');
    expect(result.reloadLinks).toBe(true);
  });

  it('keeps the server text for errors unrelated to the link', () => {
    const result = fundSaveErrorMessage(problem(400, 'Название фонда обязательно.'), base);
    expect(result).toEqual({ message: 'Название фонда обязательно.', reloadLinks: false });
  });
});
