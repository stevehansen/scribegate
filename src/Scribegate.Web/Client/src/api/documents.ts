import { apiFetch } from './client.js';
import type { DocumentResponse, DocumentListResponse } from './types.js';

export function list(repoSlug: string) {
  return apiFetch<DocumentListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/documents`,
  );
}

export function get(repoSlug: string, path: string) {
  return apiFetch<DocumentResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/documents/${path}`,
  );
}

export function create(repoSlug: string, path: string, content: string, message: string) {
  return apiFetch<DocumentResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/documents`,
    {
      method: 'POST',
      body: JSON.stringify({ path, content, message }),
    },
  );
}

export function update(repoSlug: string, path: string, content: string, message: string) {
  return apiFetch<DocumentResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/documents/${path}`,
    {
      method: 'PUT',
      body: JSON.stringify({ content, message }),
    },
  );
}

export function remove(repoSlug: string, path: string) {
  return apiFetch<void>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/documents/${path}`,
    { method: 'DELETE' },
  );
}
