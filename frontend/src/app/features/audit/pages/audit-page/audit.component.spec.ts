import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { ReplaySubject } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import { AuditComponent } from './audit.component';

describe('AuditComponent', () => {
  let httpMock: HttpTestingController;
  let queryParamMap$: ReplaySubject<ReturnType<typeof convertToParamMap>>;

  beforeEach(async () => {
    queryParamMap$ = new ReplaySubject(1);
    queryParamMap$.next(convertToParamMap({}));

    await TestBed.configureTestingModule({
      imports: [AuditComponent, HttpClientTestingModule],
      providers: [{ provide: ActivatedRoute, useValue: { queryParamMap: queryParamMap$.asObservable() } }],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('shows a prompt before any search is made', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty')?.textContent).toContain('Выберите тип записи');
  });

  it('searches by manually selected entityType/entityId', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.form.setValue({ entityType: 'Wallet', entityId: 'wallet-1' });
    component.search();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${environment.apiBaseUrl}/audit-entries` &&
        r.params.get('entityType') === 'Wallet' &&
        r.params.get('entityId') === 'wallet-1',
    );
    req.flush({
      data: [
        {
          id: 'entry-1',
          entityType: 'Wallet',
          entityId: 'wallet-1',
          action: 'Updated',
          occurredAt: '2026-01-01T10:00:00Z',
          actorKind: 'User',
          actorSystemProcess: null,
          changes: [{ field: 'name', oldValue: 'Old', newValue: 'New' }],
        },
      ],
      pagination: { nextCursor: 'cursor-1', hasMore: true },
    });
    fixture.detectChanges();

    expect(component.entries().length).toBe(1);
    expect(component.hasMore()).toBe(true);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.changes')?.textContent).toContain('Old');
    expect(compiled.querySelector('.changes')?.textContent).toContain('New');
    expect(compiled.textContent).toContain('Пользователь');
  });

  it('shows an explicit empty state (not an error) when history is empty', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.form.setValue({ entityType: 'Operation', entityId: 'operation-1' });
    component.search();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/audit-entries`)
      .flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty')?.textContent).toContain('Изменений не найдено');
    expect(component.entries().length).toBe(0);
    expect(component.loadError()).toBeNull();
  });

  it('supports load-more via cursor without duplicating entries', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.form.setValue({ entityType: 'Wallet', entityId: 'wallet-1' });
    component.search();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/audit-entries`)
      .flush({
        data: [
          {
            id: 'entry-1',
            entityType: 'Wallet',
            entityId: 'wallet-1',
            action: 'Created',
            occurredAt: '2026-01-01T10:00:00Z',
            actorKind: 'System',
            actorSystemProcess: 'BalanceSnapshotScheduler',
            changes: [],
          },
        ],
        pagination: { nextCursor: 'cursor-1', hasMore: true },
      });
    fixture.detectChanges();

    component.loadMore();
    const moreReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/audit-entries` && r.params.get('cursor') === 'cursor-1',
    );
    moreReq.flush({
      data: [
        {
          id: 'entry-2',
          entityType: 'Wallet',
          entityId: 'wallet-1',
          action: 'Updated',
          occurredAt: '2026-01-02T10:00:00Z',
          actorKind: 'User',
          actorSystemProcess: null,
          changes: [],
        },
      ],
      pagination: { nextCursor: null, hasMore: false },
    });
    fixture.detectChanges();

    expect(component.entries().length).toBe(2);
    expect(component.entries().map((entry) => entry.id)).toEqual(['entry-1', 'entry-2']);
    expect(component.hasMore()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain('Система (BalanceSnapshotScheduler)');
  });

  it('pre-fills and auto-searches from query params (link from Wallet/Operation cards)', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    queryParamMap$.next(convertToParamMap({ entityType: 'Operation', entityId: 'operation-42' }));
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${environment.apiBaseUrl}/audit-entries` &&
        r.params.get('entityType') === 'Operation' &&
        r.params.get('entityId') === 'operation-42',
    );
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(fixture.componentInstance.form.getRawValue()).toEqual({ entityType: 'Operation', entityId: 'operation-42' });
  });

  it('surfaces server errors via problem-details, not as a raw 500', () => {
    const fixture = TestBed.createComponent(AuditComponent);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.form.setValue({ entityType: 'Wallet', entityId: 'wallet-1' });
    component.search();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/audit-entries`)
      .flush({ title: 'Server error', status: 500, detail: 'Не удалось получить историю.' }, { status: 500, statusText: 'Error' });
    fixture.detectChanges();

    expect(component.loadError()).toBe('Не удалось получить историю.');
  });
});
