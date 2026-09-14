import { HttpClient } from '@angular/common/http';
import { Injectable, computed, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface LoginRequest {
  password: string;
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
}

const TOKEN_STORAGE_KEY = 'lupexwallet.auth.token';
const EXPIRES_AT_STORAGE_KEY = 'lupexwallet.auth.expiresAt';

/**
 * Authentication using a single application-wide password (docs/api/api-design.md,
 * §"Authentication"). No registration/roles — a successful login issues a JWT,
 * which is attached to all other requests via authInterceptor.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenSignal = signal<string | null>(this.readStoredToken());

  readonly isAuthenticated = computed(() => this.tokenSignal() !== null);

  constructor(private readonly http: HttpClient) {}

  login(password: string): Observable<LoginResponse> {
    const request: LoginRequest = { password };
    return this.http.post<LoginResponse>(`${environment.apiBaseUrl}/auth/login`, request).pipe(
      tap((response) => this.storeToken(response.token, response.expiresAt)),
    );
  }

  logout(): void {
    localStorage.removeItem(TOKEN_STORAGE_KEY);
    localStorage.removeItem(EXPIRES_AT_STORAGE_KEY);
    this.tokenSignal.set(null);
  }

  getToken(): string | null {
    return this.tokenSignal();
  }

  private storeToken(token: string, expiresAt: string): void {
    localStorage.setItem(TOKEN_STORAGE_KEY, token);
    localStorage.setItem(EXPIRES_AT_STORAGE_KEY, expiresAt);
    this.tokenSignal.set(token);
  }

  private readStoredToken(): string | null {
    const token = localStorage.getItem(TOKEN_STORAGE_KEY);
    const expiresAt = localStorage.getItem(EXPIRES_AT_STORAGE_KEY);
    if (!token || !expiresAt) {
      return null;
    }
    if (new Date(expiresAt).getTime() <= Date.now()) {
      return null;
    }
    return token;
  }
}
