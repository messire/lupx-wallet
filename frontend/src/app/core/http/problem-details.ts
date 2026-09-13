import { HttpErrorResponse } from '@angular/common/http';

/**
 * `application/problem+json` error body (docs/api/openapi.yaml, Problem schema,
 * RFC 7807 + an `errors[]` extension for per-field validation errors).
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
 * Extracts a user-readable error message from an `HttpErrorResponse`.
 *
 * Priority, with fallbacks for when the backend did not fill in `detail` and
 * only returned per-field `errors[]`/`title`:
 * `detail` -> first message from `errors[]` -> `title` -> `fallback`.
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
