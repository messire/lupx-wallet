import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { BudgetingApiService } from './budgeting-api.service';

const base = environment.apiBaseUrl;

describe('BudgetingApiService', () => {
  let service: BudgetingApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(BudgetingApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listBudgets passes the month filter', () => {
    service.listBudgets({ month: '2026-11' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/budgets`);
    expect(req.request.params.get('month')).toBe('2026-11');
    req.flush({ items: [], nextCursor: null });
  });

  it('setLimit sends PUT with the limit, removeLimit sends DELETE', () => {
    service.setLimit('b-1', 'cat-1', '15000.00').subscribe();
    const put = httpMock.expectOne(`${base}/budgets/b-1/limits/cat-1`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.body).toEqual({ limit: '15000.00' });
    put.flush({});

    service.removeLimit('b-1', 'cat-1').subscribe();
    const del = httpMock.expectOne(`${base}/budgets/b-1/limits/cat-1`);
    expect(del.request.method).toBe('DELETE');
    del.flush({});
  });

  it('getExecution reads /budgets/{id}/execution', () => {
    service.getExecution('b-1').subscribe();
    const req = httpMock.expectOne(`${base}/budgets/b-1/execution`);
    expect(req.request.method).toBe('GET');
    req.flush({});
  });

  it('listCategories sends includeInactive only when requested', () => {
    service.listCategories({ includeInactive: true }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/expense-categories`);
    expect(req.request.params.get('includeInactive')).toBe('true');
    req.flush({ items: [], nextCursor: null });
  });

  it('createBudget propagates 409 for a duplicate month/currency', () => {
    let status: number | undefined;
    service.createBudget({ month: '2026-11', currencyId: 'c-rub' } as never).subscribe({
      error: (error) => (status = error.status),
    });
    httpMock
      .expectOne(`${base}/budgets`)
      .flush({ title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });
    expect(status).toBe(409);
  });
});
