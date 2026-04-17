import { apiFetch } from './client.js';
import type {
  WebhookCreatedResponse,
  WebhookListResponse,
  WebhookResponse,
  WebhookDeliveryListResponse,
} from './types.js';

export interface CreateWebhookOptions {
  url: string;
  description?: string;
  events: string[];
  secret?: string;
  enabled?: boolean;
}

export interface UpdateWebhookOptions {
  url?: string;
  description?: string;
  events?: string[];
  enabled?: boolean;
  resetSecret?: boolean;
}

const base = (owner: string, slug: string) =>
  `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/webhooks`;

export function create(owner: string, repoSlug: string, options: CreateWebhookOptions) {
  return apiFetch<WebhookCreatedResponse>(base(owner, repoSlug), {
    method: 'POST',
    body: JSON.stringify(options),
  });
}

export function list(owner: string, repoSlug: string) {
  return apiFetch<WebhookListResponse>(base(owner, repoSlug));
}

export function get(owner: string, repoSlug: string, id: string) {
  return apiFetch<WebhookResponse>(`${base(owner, repoSlug)}/${id}`);
}

export function update(owner: string, repoSlug: string, id: string, options: UpdateWebhookOptions) {
  return apiFetch<WebhookResponse | WebhookCreatedResponse>(`${base(owner, repoSlug)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(options),
  });
}

export function remove(owner: string, repoSlug: string, id: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${id}`, { method: 'DELETE' });
}

export function deliveries(owner: string, repoSlug: string, id: string, take = 20) {
  return apiFetch<WebhookDeliveryListResponse>(
    `${base(owner, repoSlug)}/${id}/deliveries?take=${take}`,
  );
}

export function test(owner: string, repoSlug: string, id: string) {
  return apiFetch<void>(`${base(owner, repoSlug)}/${id}/test`, { method: 'POST' });
}

export const EVENT_TYPES = [
  'proposal.created',
  'proposal.submitted',
  'proposal.approved',
  'proposal.rejected',
  'proposal.withdrawn',
  'document.created',
  'document.updated',
  'document.deleted',
  'document.moved',
  'review.submitted',
  'comment.created',
] as const;
