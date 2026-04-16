import { apiFetch } from './client.js';
import type {
  CreateTemplateRequest,
  TemplateListResponse,
  TemplateResponse,
  UpdateTemplateRequest,
} from './types.js';

const base = (slug: string) => `/api/v1/repositories/${encodeURIComponent(slug)}/templates`;

export function list(repoSlug: string) {
  return apiFetch<TemplateListResponse>(base(repoSlug));
}

export function get(repoSlug: string, id: string) {
  return apiFetch<TemplateResponse>(`${base(repoSlug)}/${id}`);
}

export function create(repoSlug: string, request: CreateTemplateRequest) {
  return apiFetch<TemplateResponse>(base(repoSlug), {
    method: 'POST',
    body: JSON.stringify(request),
  });
}

export function update(repoSlug: string, id: string, request: UpdateTemplateRequest) {
  return apiFetch<TemplateResponse>(`${base(repoSlug)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(request),
  });
}

export function remove(repoSlug: string, id: string) {
  return apiFetch<void>(`${base(repoSlug)}/${id}`, { method: 'DELETE' });
}
