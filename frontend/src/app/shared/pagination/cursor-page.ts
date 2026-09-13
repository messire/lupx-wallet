import { HttpParams } from '@angular/common/http';

/**
 * Курсорная пагинация (docs/api/openapi.yaml, схема CursorPageMeta) — общий
 * контракт для всех списковых эндпоинтов (Wallets, ReferenceData, Operations,
 * BalanceHistory, ...). Ранее типы дублировались в моделях каждой фичи
 * (см. wallets.models.ts / reference-data.models.ts до этого среза).
 */
export interface CursorPageMeta {
  nextCursor: string | null;
  hasMore: boolean;
}

export interface CursorPage<T> {
  data: T[];
  pagination: CursorPageMeta;
}

export interface CursorPageOptions {
  cursor?: string | null;
  limit?: number;
}

/** Добавляет `cursor`/`limit` к уже собранным `HttpParams` запроса списка. */
export function toCursorParams(options: CursorPageOptions, params: HttpParams = new HttpParams()): HttpParams {
  let result = params;
  if (options.cursor) {
    result = result.set('cursor', options.cursor);
  }
  if (options.limit) {
    result = result.set('limit', String(options.limit));
  }
  return result;
}

export interface LoadMoreResult<T> {
  items: T[];
  nextCursor: string | null;
  hasMore: boolean;
}

/**
 * Хелпер «загрузить ещё»: присоединяет новую страницу к уже загруженным
 * элементам и возвращает обновленное состояние курсора/наличия следующей страницы.
 */
export function appendPage<T>(existingItems: T[], page: CursorPage<T>): LoadMoreResult<T> {
  return {
    items: [...existingItems, ...page.data],
    nextCursor: page.pagination.nextCursor,
    hasMore: page.pagination.hasMore,
  };
}
