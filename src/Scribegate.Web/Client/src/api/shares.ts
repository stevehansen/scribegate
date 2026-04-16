import { apiFetch } from './client.js';
import type {
  ShareLinkCreatedResponse,
  ShareLinkListResponse,
  PublicShareLinkResponse,
} from './types.js';

export interface CreateShareLinkOptions {
  path: string;
  description?: string;
  expiresInDays?: number;
  permanent?: boolean;
  revisionId?: string;
}

export function create(repoSlug: string, options: CreateShareLinkOptions) {
  return apiFetch<ShareLinkCreatedResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/shares`,
    {
      method: 'POST',
      body: JSON.stringify(options),
    },
  );
}

export function list(repoSlug: string, path?: string) {
  const query = path ? `?path=${encodeURIComponent(path)}` : '';
  return apiFetch<ShareLinkListResponse>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/shares${query}`,
  );
}

export function revoke(repoSlug: string, id: string) {
  return apiFetch<void>(
    `/api/v1/repositories/${encodeURIComponent(repoSlug)}/shares/${id}`,
    { method: 'DELETE' },
  );
}

export function resolve(token: string) {
  return apiFetch<PublicShareLinkResponse>(
    `/api/v1/shares/${encodeURIComponent(token)}`,
  );
}
