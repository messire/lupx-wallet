import { HttpErrorResponse } from '@angular/common/http';
import { extractErrorMessage } from './problem-details';

const FALLBACK = 'Что-то пошло не так';

function problemError(body: unknown, status = 400): HttpErrorResponse {
  return new HttpErrorResponse({ error: body, status, statusText: 'Bad Request' });
}

describe('extractErrorMessage', () => {
  it('returns detail when problem+json body has detail', () => {
    const error = problemError({ detail: 'Кошелек не найден' });

    expect(extractErrorMessage(error, FALLBACK)).toBe('Кошелек не найден');
  });

  it('returns first field error message when detail is absent but errors[] is present', () => {
    const error = problemError({
      title: 'Validation failed',
      errors: [
        { field: 'amount', message: undefined },
        { field: 'currencyId', message: 'Валюта обязательна' },
        { field: 'walletId', message: 'Кошелек обязателен' },
      ],
    });

    expect(extractErrorMessage(error, FALLBACK)).toBe('Валюта обязательна');
  });

  it('returns title when detail and errors[] are both absent', () => {
    const error = problemError({ title: 'Not Found' });

    expect(extractErrorMessage(error, FALLBACK)).toBe('Not Found');
  });

  it('returns fallback when body has no detail, errors, or title', () => {
    const error = problemError({ type: 'about:blank', status: 400 });

    expect(extractErrorMessage(error, FALLBACK)).toBe(FALLBACK);
  });

  it('returns fallback when response has no body (null)', () => {
    const error = problemError(null);

    expect(extractErrorMessage(error, FALLBACK)).toBe(FALLBACK);
  });

  it('returns fallback when body is not an object (invalid/non-JSON error body)', () => {
    const error = problemError('Internal Server Error', 500);

    expect(extractErrorMessage(error, FALLBACK)).toBe(FALLBACK);
  });

  it('returns fallback when error is not an HttpErrorResponse at all', () => {
    expect(extractErrorMessage(new Error('network down'), FALLBACK)).toBe(FALLBACK);
    expect(extractErrorMessage(undefined, FALLBACK)).toBe(FALLBACK);
  });

  it('prefers detail over errors[] and title when all three are present', () => {
    const error = problemError({
      detail: 'Приоритетное сообщение',
      title: 'Validation failed',
      errors: [{ field: 'amount', message: 'Сумма обязательна' }],
    });

    expect(extractErrorMessage(error, FALLBACK)).toBe('Приоритетное сообщение');
  });

  it('skips field errors without a message and falls back to title', () => {
    const error = problemError({
      title: 'Validation failed',
      errors: [{ field: 'amount', message: undefined }],
    });

    expect(extractErrorMessage(error, FALLBACK)).toBe('Validation failed');
  });
});
