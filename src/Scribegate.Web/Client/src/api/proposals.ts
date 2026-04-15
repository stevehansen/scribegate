import { apiFetch } from './client.js';
import type { ProposalResponse, ProposalListResponse, ProposalSummary } from './types.js';

export function list(repoSlug: string, status?: string) {
  const params = status ? `?status=${status}` : '';
  return apiFetch<ProposalListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals${params}`,
  );
}

export function get(repoSlug: string, id: string) {
  return apiFetch<ProposalResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}`,
  );
}

export function create(repoSlug: string, data: { title: string; content: string; documentPath?: string; documentId?: string; description?: string }) {
  return apiFetch<ProposalSummary>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals`,
    { method: 'POST', body: JSON.stringify(data) },
  );
}

export function update(repoSlug: string, id: string, data: { title?: string; description?: string; content?: string }) {
  return apiFetch<ProposalSummary>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}`,
    { method: 'PUT', body: JSON.stringify(data) },
  );
}

export function submit(repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}/submit`,
    { method: 'POST' },
  );
}

export function withdraw(repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}/withdraw`,
    { method: 'POST' },
  );
}

export function approve(repoSlug: string, id: string) {
  return apiFetch<{ status: string; revisionId: string }>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}/approve`,
    { method: 'POST' },
  );
}

export function reject(repoSlug: string, id: string) {
  return apiFetch<{ status: string }>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${id}/reject`,
    { method: 'POST' },
  );
}
