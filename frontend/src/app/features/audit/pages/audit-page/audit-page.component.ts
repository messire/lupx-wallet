import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { AUDIT_ENTITY_TYPES, AuditEntry } from '../../../../data-access/audit/audit-api.models';
import { AuditApiService } from '../../../../data-access/audit/audit-api.service';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';

/**
 * UC-25 — standalone `/audit` screen: pick an entity type + manually enter its
 * id, table of change history with cursor-based "load more" pagination.
 *
 * The same component/route is also opened via the "change history" link from
 * Wallet/Operation cards — entityType/entityId arrive as query params
 * (`?entityType=Wallet&entityId=...`) rather than a separate route, so
 * app.routes.ts only needed the reserved `/audit` entry. Query params are read
 * reactively (`queryParamMap`) because Angular reuses this component when
 * navigating to a different entityId under the same route configuration.
 */
@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    FieldComponent,
    FieldControlDirective,
    EmptyStateComponent,
    LoadingStateComponent,
    SkeletonComponent,
    ErrorStateComponent,
  ],
  templateUrl: './audit-page.component.html',
  styleUrl: './audit-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditComponent implements OnInit {
  private readonly auditService = inject(AuditApiService);
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
  /** Distinguishes "not searched yet" from "searched and found nothing" for the empty state. */
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

  /** actorKind=User → "Пользователь"; System → "Система (имя процесса)". */
  actorLabel(entry: AuditEntry): string {
    if (entry.actorKind !== 'System') {
      return 'Пользователь';
    }
    return entry.actorSystemProcess ? `Система (${entry.actorSystemProcess})` : 'Система';
  }
}
