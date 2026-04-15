import { apiFetch } from './client.js';
import type { RevisionListResponse, RevisionResponse } from './types.js';

export function list(repoSlug: string, docPath: string) {
  return apiFetch<RevisionListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/revisions/${docPath}`,
  );
}

export function get(repoSlug: string, documentId: string, revisionId: string) {
  return apiFetch<RevisionResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/revisions/${documentId}/${revisionId}`,
  );
}
