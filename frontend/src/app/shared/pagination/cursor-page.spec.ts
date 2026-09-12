import { HttpParams } from '@angular/common/http';
import { appendPage, CursorPage, toCursorParams } from './cursor-page';

describe('toCursorParams', () => {
  it('adds nothing when no cursor or limit are provided', () => {
    const params = toCursorParams({});

    expect(params.has('cursor')).toBe(false);
    expect(params.has('limit')).toBe(false);
  });

  it('adds the cursor param when a cursor is provided', () => {
    const params = toCursorParams({ cursor: 'abc123' });

    expect(params.get('cursor')).toBe('abc123');
  });

  it('adds the limit param as a string when a limit is provided', () => {
    const params = toCursorParams({ limit: 20 });

    expect(params.get('limit')).toBe('20');
  });

  it('adds both cursor and limit when both are provided', () => {
    const params = toCursorParams({ cursor: 'abc123', limit: 20 });

    expect(params.get('cursor')).toBe('abc123');
    expect(params.get('limit')).toBe('20');
  });

  it('does not add an empty-string cursor', () => {
    const params = toCursorParams({ cursor: '' });

    expect(params.has('cursor')).toBe(false);
  });

  it('does not add a zero limit', () => {
    const params = toCursorParams({ limit: 0 });

    expect(params.has('limit')).toBe(false);
  });

  it('merges into an existing HttpParams instance without dropping existing params', () => {
    const existing = new HttpParams().set('walletId', 'w-1');

    const params = toCursorParams({ cursor: 'abc123' }, existing);

    expect(params.get('walletId')).toBe('w-1');
    expect(params.get('cursor')).toBe('abc123');
  });
});

describe('appendPage', () => {
  it('appends the new page data after the existing items', () => {
    const page: CursorPage<number> = { data: [3, 4], pagination: { nextCursor: 'next-1', hasMore: true } };

    const result = appendPage([1, 2], page);

    expect(result.items).toEqual([1, 2, 3, 4]);
  });

  it('carries over the next cursor and hasMore flag from the page', () => {
    const page: CursorPage<number> = { data: [3], pagination: { nextCursor: 'next-1', hasMore: true } };

    const result = appendPage([1, 2], page);

    expect(result.nextCursor).toBe('next-1');
    expect(result.hasMore).toBe(true);
  });

  it('reports hasMore=false and nextCursor=null when the page is the last one', () => {
    const page: CursorPage<number> = { data: [3], pagination: { nextCursor: null, hasMore: false } };

    const result = appendPage([1, 2], page);

    expect(result.nextCursor).toBeNull();
    expect(result.hasMore).toBe(false);
  });

  it('does not mutate the existing items array', () => {
    const existing = [1, 2];
    const page: CursorPage<number> = { data: [3], pagination: { nextCursor: null, hasMore: false } };

    const result = appendPage(existing, page);

    expect(existing).toEqual([1, 2]);
    expect(result.items).not.toBe(existing);
  });

  it('returns the page data unchanged when there are no existing items', () => {
    const page: CursorPage<number> = { data: [1, 2], pagination: { nextCursor: null, hasMore: false } };

    const result = appendPage([], page);

    expect(result.items).toEqual([1, 2]);
  });
});
