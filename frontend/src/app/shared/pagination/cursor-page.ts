import { HttpParams } from '@angular/common/http';

/**
 * Cursor pagination (docs/api/openapi.yaml, CursorPageMeta schema) — the shared
 * contract for all list endpoints (Wallets, ReferenceData, Operations,
 * BalanceHistory, ...).
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

/** Adds `cursor`/`limit` to the already-built `HttpParams` of a list request. */
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
 * "Load more" helper: appends a new page to the already-loaded items and
 * returns the updated cursor/has-more-pages state.
 */
export function appendPage<T>(existingItems: T[], page: CursorPage<T>): LoadMoreResult<T> {
  return {
    items: [...existingItems, ...page.data],
    nextCursor: page.pagination.nextCursor,
    hasMore: page.pagination.hasMore,
  };
}
