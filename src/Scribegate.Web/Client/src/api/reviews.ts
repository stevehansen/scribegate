import { apiFetch } from './client.js';
import type { ReviewResponse, ReviewListResponse } from './types.js';

const base = (owner: string, slug: string, proposalId: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/proposals/${proposalId}/reviews`;

export function list(owner: string, repoSlug: string, proposalId: string) {
  return apiFetch<ReviewListResponse>(base(owner, repoSlug, proposalId));
}

export function create(owner: string, repoSlug: string, proposalId: string, data: { verdict: string; body?: string }) {
  return apiFetch<ReviewResponse>(base(owner, repoSlug, proposalId), {
    method: 'POST',
    body: JSON.stringify(data),
  });
}
