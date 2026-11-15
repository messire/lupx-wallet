import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { BudgetsComponent } from './budgets-page.component';

const api = environment.apiBaseUrl;
const emptyPage = { data: [], pagination: { nextCursor: null, hasMore: false } };
const budget = (id: string, month: string) => ({ id, month, currencyId: 'c-rub', limits: [] });
const page = (data: unknown[]) => ({ data, pagination: { nextCursor: null, hasMore: false } });

describe('BudgetsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BudgetsComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('ignores a budget list that arrives after the user switched to another month', () => {
    const fixture = TestBed.createComponent(BudgetsComponent);
    fixture.detectChanges();
    httpMock
      .match((req) => !req.url.startsWith(`${api}/budgets`))
      .forEach((req) => req.flush(req.request.url.endsWith('/operation-behavior-kinds') ? [] : emptyPage));
    const component = fixture.componentInstance;
    const firstMonth = component.month();
    const first = httpMock.expectOne((req) => req.url === `${api}/budgets` && req.params.get('month') === firstMonth);

    component.nextMonth();
    const secondMonth = component.month();
    const second = httpMock.expectOne((req) => req.url === `${api}/budgets` && req.params.get('month') === secondMonth);

    second.flush(page([budget('b-new', secondMonth)]));
    httpMock.expectOne(`${api}/budgets/b-new/execution`);
    first.flush(page([budget('b-old', firstMonth)]));

    expect(component.selectedBudgetId()).toBe('b-new');
    expect(component.budgets().map((b) => b.id)).toEqual(['b-new']);
  });
});
