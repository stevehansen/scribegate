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

const base = (slug: string) => `/api/v1/repositories/${encodeURIComponent(slug)}/webhooks`;

export function create(repoSlug: string, options: CreateWebhookOptions) {
  return apiFetch<WebhookCreatedResponse>(base(repoSlug), {
    method: 'POST',
    body: JSON.stringify(options),
  });
}

export function list(repoSlug: string) {
  return apiFetch<WebhookListResponse>(base(repoSlug));
}

export function get(repoSlug: string, id: string) {
  return apiFetch<WebhookResponse>(`${base(repoSlug)}/${id}`);
}

export function update(repoSlug: string, id: string, options: UpdateWebhookOptions) {
  return apiFetch<WebhookResponse | WebhookCreatedResponse>(`${base(repoSlug)}/${id}`, {
    method: 'PUT',
    body: JSON.stringify(options),
  });
}

export function remove(repoSlug: string, id: string) {
  return apiFetch<void>(`${base(repoSlug)}/${id}`, { method: 'DELETE' });
}

export function deliveries(repoSlug: string, id: string, take = 20) {
  return apiFetch<WebhookDeliveryListResponse>(`${base(repoSlug)}/${id}/deliveries?take=${take}`);
}

export function test(repoSlug: string, id: string) {
  return apiFetch<void>(`${base(repoSlug)}/${id}/test`, { method: 'POST' });
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
