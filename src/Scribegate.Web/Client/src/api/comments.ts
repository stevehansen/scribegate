import { apiFetch } from './client.js';
import type { CommentResponse, CommentListResponse } from './types.js';

const base = (owner: string, slug: string, proposalId: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/proposals/${proposalId}/comments`;

export function list(owner: string, repoSlug: string, proposalId: string) {
  return apiFetch<CommentListResponse>(base(owner, repoSlug, proposalId));
}

export function create(owner: string, repoSlug: string, proposalId: string, data: { body: string; parentCommentId?: string; lineReference?: number }) {
  return apiFetch<CommentResponse>(base(owner, repoSlug, proposalId), {
    method: 'POST',
    body: JSON.stringify(data),
  });
}

export function update(owner: string, repoSlug: string, proposalId: string, id: string, data: { body: string }) {
  return apiFetch<CommentResponse>(`${base(owner, repoSlug, proposalId)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(data),
  });
}

export function remove(owner: string, repoSlug: string, proposalId: string, id: string) {
  return apiFetch<void>(`${base(owner, repoSlug, proposalId)}/${id}`, { method: 'DELETE' });
}
