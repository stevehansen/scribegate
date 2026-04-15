import { apiFetch } from './client.js';
import type { AuthResponse, UserInfo, ApiTokenResponse, ApiTokenCreatedResponse } from './types.js';

export function register(username: string, email: string, password: string, acceptTos: boolean) {
  return apiFetch<AuthResponse>('/api/v1/auth/register', {
    method: 'POST',
    body: JSON.stringify({ username, email, password, acceptTos }),
  });
}

export function login(email: string, password: string) {
  return apiFetch<AuthResponse>('/api/v1/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  });
}

export function getMe() {
  return apiFetch<UserInfo>('/api/v1/auth/me');
}

export function updatePreferences(prefs: { themePreference?: string }) {
  return apiFetch<UserInfo>('/api/v1/auth/preferences', {
    method: 'PUT',
    body: JSON.stringify(prefs),
  });
}

export function createApiToken(name: string, expiresInDays?: number) {
  return apiFetch<ApiTokenCreatedResponse>('/api/v1/auth/tokens', {
    method: 'POST',
    body: JSON.stringify({ name, expiresInDays }),
  });
}

export function listApiTokens() {
  return apiFetch<ApiTokenResponse[]>('/api/v1/auth/tokens');
}

export function deleteApiToken(id: string) {
  return apiFetch<void>(`/api/v1/auth/tokens/${id}`, { method: 'DELETE' });
}
