import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { appendPage } from '../../core/api/cursor-page';
import { extractErrorMessage } from '../../core/http/problem-details';
import { AUDIT_ENTITY_TYPES, AuditEntry } from './audit.models';
import { AuditService } from './audit.service';

/**
 * UC-25 — самостоятельный экран `/audit`: выбор типа записи + ручной ввод её
 * идентификатора (docs/PROGRESS.md, W3.2 — "простой вариант для самостоятельного
 * экрана"), таблица истории изменений с курсорной пагинацией "загрузить ещё".
 *
 * Этот же компонент/маршрут открывается по ссылке "История изменений" из
 * карточек Wallet/Operation — entityType/entityId приходят как query-параметры
 * (`?entityType=Wallet&entityId=...`), а не как отдельный маршрут, поэтому
 * app.routes.ts не менялся сверх зарезервированной записи `/audit`. Чтение
 * query-параметров реактивное (`queryParamMap`), так как Angular переиспользует
 * этот компонент при переходе с другим entityId по той же конфигурации маршрута.
 */
@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './audit.component.html',
  styleUrl: './audit.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditComponent implements OnInit {
  private readonly auditService = inject(AuditService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);

  readonly entityTypes = AUDIT_ENTITY_TYPES;

  readonly form = this.formBuilder.nonNullable.group({
    entityType: ['', Validators.required],
    entityId: ['', Validators.required],
  });

  readonly entries = signal<AuditEntry[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  /** Различает "ещё не искали" от "искали и ничего не нашли" — для пустого состояния (DoD п.3). */
  readonly hasSearched = signal(false);

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      const entityType = params.get('entityType');
      const entityId = params.get('entityId');
      if (entityType && entityId) {
        this.form.setValue({ entityType, entityId });
        this.search();
      }
    });
  }

  search(): void {
    if (this.form.invalid || this.isLoading()) {
      return;
    }

    const { entityType, entityId } = this.form.getRawValue();
    this.isLoading.set(true);
    this.loadError.set(null);

    this.auditService.list(entityType, entityId).subscribe({
      next: (page) => {
        this.entries.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.isLoading.set(false);
        this.hasSearched.set(true);
      },
      error: (error: unknown) => {
        this.entries.set([]);
        this.nextCursor.set(null);
        this.hasMore.set(false);
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить историю изменений.'));
        this.isLoading.set(false);
        this.hasSearched.set(true);
      },
    });
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    const { entityType, entityId } = this.form.getRawValue();
    if (!cursor || this.isLoading() || !entityType || !entityId) {
      return;
    }

    this.isLoading.set(true);
    this.auditService.list(entityType, entityId, { cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.entries(), page);
        this.entries.set(result.items);
        this.nextCursor.set(result.nextCursor);
        this.hasMore.set(result.hasMore);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить следующую страницу.'));
        this.isLoading.set(false);
      },
    });
  }

  /** actorKind=User → "Пользователь"; System → "Система (имя процесса)" (DoD п.4). */
  actorLabel(entry: AuditEntry): string {
    if (entry.actorKind !== 'System') {
      return 'Пользователь';
    }
    return entry.actorSystemProcess ? `Система (${entry.actorSystemProcess})` : 'Система';
  }
}
