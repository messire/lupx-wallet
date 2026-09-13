import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastComponent } from './toast.component';

describe('ToastComponent', () => {
  let fixture: ComponentFixture<ToastComponent>;
  let component: ToastComponent;

  beforeEach(async () => {
    vi.useFakeTimers();

    await TestBed.configureTestingModule({
      imports: [ToastComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ToastComponent);
    component = fixture.componentInstance;
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function create(message: string, tone: 'success' | 'info' | 'error' = 'info', autoDismiss = true): void {
    fixture.componentRef.setInput('message', message);
    fixture.componentRef.setInput('tone', tone);
    fixture.componentRef.setInput('autoDismiss', autoDismiss);
    fixture.detectChanges();
  }

  it('auto-dismisses after 6 seconds for an info toast', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Сохранено', 'info');

    vi.advanceTimersByTime(5999);
    expect(dismissedCount).toBe(0);

    vi.advanceTimersByTime(1);
    expect(dismissedCount).toBe(1);
  });

  it('auto-dismisses after 6 seconds for a success toast', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Операция выполнена', 'success');

    vi.advanceTimersByTime(6000);

    expect(dismissedCount).toBe(1);
  });

  it('does not auto-dismiss an error toast even after a long time', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Не удалось сохранить', 'error');

    vi.advanceTimersByTime(60_000);

    expect(dismissedCount).toBe(0);
  });

  it('does not auto-dismiss an error toast even when autoDismiss=true is explicitly set', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Критическая ошибка', 'error', true);

    vi.advanceTimersByTime(60_000);

    expect(dismissedCount).toBe(0);
  });

  it('does not auto-dismiss a non-error toast when autoDismiss=false', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Информация', 'info', false);

    vi.advanceTimersByTime(60_000);

    expect(dismissedCount).toBe(0);
  });

  it('emits dismissed immediately when the close button is clicked', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Сохранено', 'error');

    (fixture.nativeElement.querySelector('.app-toast__close') as HTMLButtonElement).click();

    expect(dismissedCount).toBe(1);
  });

  it('clears the auto-dismiss timer on destroy so it never fires after the component is gone', () => {
    let dismissedCount = 0;
    component.dismissed.subscribe(() => dismissedCount++);
    create('Сохранено', 'info');

    fixture.destroy();
    vi.advanceTimersByTime(10_000);

    expect(dismissedCount).toBe(0);
  });
});
