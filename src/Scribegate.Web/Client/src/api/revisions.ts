import { apiFetch } from './client.js';
import type { RevisionListResponse, RevisionResponse } from './types.js';

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/revisions`;

export function list(owner: string, repoSlug: string, docPath: string) {
  return apiFetch<RevisionListResponse>(`${base(owner, repoSlug)}/${docPath}`);
}

export function get(owner: string, repoSlug: string, documentId: string, revisionId: string) {
  return apiFetch<RevisionResponse>(`${base(owner, repoSlug)}/${documentId}/${revisionId}`);
}
