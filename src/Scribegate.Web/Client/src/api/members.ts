import { apiFetch } from './client.js';
import type { MemberResponse, MemberListResponse } from './types.js';

export function list(repoSlug: string) {
  return apiFetch<MemberListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/members`,
  );
}

export function add(repoSlug: string, data: { username: string; role: string }) {
  return apiFetch<MemberResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/members`,
    { method: 'POST', body: JSON.stringify(data) },
  );
}

export function updateRole(repoSlug: string, userId: string, data: { role: string }) {
  return apiFetch<MemberResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/members/${userId}`,
    { method: 'PUT', body: JSON.stringify(data) },
  );
}

export function remove(repoSlug: string, userId: string) {
  return apiFetch<void>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/members/${userId}`,
    { method: 'DELETE' },
  );
}
