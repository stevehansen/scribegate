import { apiFetch } from './client.js';
import type { ProposalResponse, ProposalListResponse, ProposalSummary } from './types.js';

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/proposals`;

export function list(owner: string, repoSlug: string, status?: string) {
  const params = status ? `?status=${status}` : '';
  return apiFetch<ProposalListResponse>(`${base(owner, repoSlug)}${params}`);
}

export function get(owner: string, repoSlug: string, id: string) {
  return apiFetch<ProposalResponse>(`${base(owner, repoSlug)}/${id}`);
}

export function create(owner: string, repoSlug: string, data: { title: string; content: string; documentPath?: string; documentId?: string; description?: string }) {
  return apiFetch<ProposalSummary>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function update(owner: string, repoSlug: string, id: string, data: { title?: string; description?: string; content?: string }) {
  return apiFetch<ProposalSummary>(`${base(owner, repoSlug)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function submit(owner: string, repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(`${base(owner, repoSlug)}/${id}/submit`, { method: 'POST' });
}

export function withdraw(owner: string, repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(`${base(owner, repoSlug)}/${id}/withdraw`, { method: 'POST' });
}

export function approve(owner: string, repoSlug: string, id: string) {
  return apiFetch<{ status: string; revisionId: string }>(
    `${base(owner, repoSlug)}/${id}/approve`,
    { method: 'POST' },
  );
}

export function reject(owner: string, repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(`${base(owner, repoSlug)}/${id}/reject`, { method: 'POST' });
}
