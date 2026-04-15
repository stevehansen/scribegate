import { apiFetch } from './client.js';
import type { RepositoryResponse, RepositoryListResponse } from './types.js';

export function list() {
  return apiFetch<RepositoryListResponse>('/api/v1/repositories');
}

export function get(slug: string) {
  return apiFetch<RepositoryResponse>(`/api/v1/repositories/${encodeURIComponent(slug)}`);
}

export function create(name: string, description?: string, visibility?: string) {
  return apiFetch<RepositoryResponse>('/api/v1/repositories', {
    method: 'POST',
    body: JSON.stringify({ name, description, visibility }),
  });
}

export function update(slug: string, data: { name?: string; description?: string; visibility?: string }) {
  return apiFetch<RepositoryResponse>(`/api/v1/repositories/${encodeURIComponent(slug)}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function remove(slug: string) {
  return apiFetch<void>(`/api/v1/repositories/${encodeURIComponent(slug)}`, {
    method: 'DELETE',
  });
}
