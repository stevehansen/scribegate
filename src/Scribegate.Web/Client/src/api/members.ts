import { apiFetch } from './client.js';
import type { MemberResponse, MemberListResponse } from './types.js';

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/members`;

export function list(owner: string, repoSlug: string) {
  return apiFetch<MemberListResponse>(base(owner, repoSlug));
}

export function add(owner: string, repoSlug: string, data: { username: string; role: string }) {
  return apiFetch<MemberResponse>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function updateRole(owner: string, repoSlug: string, userId: string, data: { role: string }) {
  return apiFetch<MemberResponse>(`${base(owner, repoSlug)}/${userId}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function remove(owner: string, repoSlug: string, userId: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${userId}`, { method: 'DELETE' });
}
