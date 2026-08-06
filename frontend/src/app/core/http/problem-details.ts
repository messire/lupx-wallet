import { HttpErrorResponse } from '@angular/common/http';

/**
 * Тело ошибки `application/problem+json` (docs/api/openapi.yaml, схема Problem,
 * RFC 7807 + расширение `errors[]` для постатейных ошибок валидации).
 */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  errors?: Array<{ field?: string; message?: string }>;
}

/**
 * Извлекает читаемое пользователю сообщение об ошибке из `HttpErrorResponse`.
 *
 * Приоритет (совпадает с ранее продублированным паттерном `error.error?.detail`
 * из wallets.component.ts, расширенным резервными вариантами для случаев, когда
 * бэкенд не заполнил `detail`, а вернул только постатейные `errors[]`/`title`):
 * `detail` → первое сообщение из `errors[]` → `title` → `fallback`.
 */
export function extractErrorMessage(error: unknown, fallback: string): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  const problem = asProblemDetails(error.error);
  if (!problem) {
    return fallback;
  }

  const firstFieldError = problem.errors?.find((item) => !!item.message)?.message;
  return problem.detail ?? firstFieldError ?? problem.title ?? fallback;
}

function asProblemDetails(body: unknown): ProblemDetails | null {
  if (!body || typeof body !== 'object') {
    return null;
  }
  return body as ProblemDetails;
}
