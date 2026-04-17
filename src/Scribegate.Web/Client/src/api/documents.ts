import { apiFetch } from './client.js';
import type { DocumentResponse, DocumentListResponse } from './types.js';

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/documents`;

export function list(owner: string, repoSlug: string) {
  return apiFetch<DocumentListResponse>(base(owner, repoSlug));
}

export function get(owner: string, repoSlug: string, path: string) {
  return apiFetch<DocumentResponse>(`${base(owner, repoSlug)}/${path}`);
}

export function create(owner: string, repoSlug: string, path: string, content: string, message: string) {
  return apiFetch<DocumentResponse>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify({ path, content, message }),
  });
}

export function update(owner: string, repoSlug: string, path: string, content: string, message: string) {
  return apiFetch<DocumentResponse>(`${base(owner, repoSlug)}/${path}`, {
    method: 'PUT',
    body: JSON.stringify({ content, message }),
  });
}

export function remove(owner: string, repoSlug: string, path: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${path}`, { method: 'DELETE' });
}
