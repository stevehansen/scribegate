import { apiFetch } from './client.js';
import type { CommentResponse, CommentListResponse } from './types.js';

export function list(repoSlug: string, proposalId: string) {
  return apiFetch<CommentListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/comments`,
  );
}

export function create(repoSlug: string, proposalId: string, data: { body: string; parentCommentId?: string; lineReference?: number }) {
  return apiFetch<CommentResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/comments`,
    { method: 'POST', body: JSON.stringify(data) },
  );
}

export function update(repoSlug: string, proposalId: string, id: string, data: { body: string }) {
  return apiFetch<CommentResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/comments/${id}`,
    { method: 'PUT', body: JSON.stringify(data) },
  );
}

export function remove(repoSlug: string, proposalId: string, id: string) {
  return apiFetch<void>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/proposals/${proposalId}/comments/${id}`,
    { method: 'DELETE' },
  );
}
