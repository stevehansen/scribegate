import { apiFetch } from './client.js';
import type { ReviewResponse, ReviewListResponse } from './types.js';

export function list(repoSlug: string, proposalId: string) {
  return apiFetch<ReviewListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/reviews`,
  );
}

export function create(repoSlug: string, proposalId: string, data: { verdict: string; body?: string }) {
  return apiFetch<ReviewResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/reviews`,
    { method: 'POST', body: JSON.stringify(data) },
  );
}
