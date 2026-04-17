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

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/shares`;

export function create(owner: string, repoSlug: string, options: CreateShareLinkOptions) {
  return apiFetch<ShareLinkCreatedResponse>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify(options),
  });
}

export function list(owner: string, repoSlug: string, path?: string) {
  const query = path ? `?path=${encodeURIComponent(path)}` : '';
  return apiFetch<ShareLinkListResponse>(`${base(owner, repoSlug)}${query}`);
}

export function revoke(owner: string, repoSlug: string, id: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${id}`, { method: 'DELETE' });
}

export function resolve(token: string) {
  return apiFetch<PublicShareLinkResponse>(
    `/api/v1/shares/${encodeURIComponent(token)}`,
  );
}
