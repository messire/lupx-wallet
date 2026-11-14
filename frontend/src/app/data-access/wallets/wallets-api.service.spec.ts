import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { CreateWalletRequest, Wallet } from './wallets-api.models';
import { WalletsApiService } from './wallets-api.service';

const created: Wallet = {
  id: 'w-1',
  name: 'Евро',
  walletTypeId: 'wt-1',
  purposeDescription: null,
  currencyId: 'c-eur',
  initialBalance: { amount: '10', currencyId: 'c-eur' },
  accountingStartDate: '2026-09-01',
  currentBalance: { amount: '10', currencyId: 'c-eur' },
  includeInTotal: true,
  isPrimary: false,
  isArchived: false,
  displayOrder: 0,
  color: null,
  icon: null,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
};

describe('WalletsApiService (ADR-0004 §7: wallets are created by currency code)', () => {
  let service: WalletsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(WalletsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('create sends POST /wallets with currencyCode and without currencyId', () => {
    const request: CreateWalletRequest = {
      name: 'Евро',
      walletTypeId: 'wt-1',
      currencyCode: 'EUR',
      initialBalanceAmount: '10',
      accountingStartDate: '2026-09-01',
      includeInTotal: true,
      displayOrder: 0,
    };
    let result: Wallet | undefined;
    service.create(request).subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(request);
    expect(req.request.body.currencyCode).toBe('EUR');
    expect('currencyId' in req.request.body).toBe(false);
    req.flush(created, { status: 201, statusText: 'Created' });

    expect(result).toEqual(created);
  });

  it('create propagates 400 (currency is not available) to the subscriber', () => {
    let status: number | undefined;
    service
      .create({ name: 'X', walletTypeId: 'wt-1', currencyCode: 'XXX', initialBalanceAmount: '0', accountingStartDate: '2026-09-01' })
      .subscribe({ error: (error) => (status = error.status) });

    httpMock.expectOne(`${environment.apiBaseUrl}/wallets`).flush(null, { status: 400, statusText: 'Bad Request' });

    expect(status).toBe(400);
  });
});
