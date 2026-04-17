import { apiFetch } from './client.js';
import type {
  CreateTemplateRequest,
  TemplateListResponse,
  TemplateResponse,
  UpdateTemplateRequest,
} from './types.js';

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/templates`;

export function list(owner: string, repoSlug: string) {
  return apiFetch<TemplateListResponse>(base(owner, repoSlug));
}

export function get(owner: string, repoSlug: string, id: string) {
  return apiFetch<TemplateResponse>(`${base(owner, repoSlug)}/${id}`);
}

export function create(owner: string, repoSlug: string, request: CreateTemplateRequest) {
  return apiFetch<TemplateResponse>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function update(owner: string, repoSlug: string, id: string, request: UpdateTemplateRequest) {
  return apiFetch<TemplateResponse>(`${base(owner, repoSlug)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function remove(owner: string, repoSlug: string, id: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${id}`, { method: 'DELETE' });
}
