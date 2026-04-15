import { apiFetch } from './client.js';
import type { SettingResponse, AuditEventListResponse } from './types.js';

export function getRegistrationStatus() {
  return apiFetch<{ registrationEnabled: boolean }>('/api/v1/admin/settings/registration');
}

export function listSettings() {
  return apiFetch<SettingResponse[]>('/api/v1/admin/settings');
}

export function updateSetting(key: string, value: string) {
  return apiFetch<SettingResponse>(`/api/v1/admin/settings/${encodeURIComponent(key)}`, {
    method: 'PUT',
    body: JSON.stringify({ value }),
  });
}

export function listAuditEvents(params?: { eventType?: string; targetType?: string; skip?: number; take?: number }) {
  const query = new URLSearchParams();
  if (params?.eventType) query.set('eventType', params.eventType);
  if (params?.targetType) query.set('targetType', params.targetType);
  if (params?.skip) query.set('skip', params.skip.toString());
  if (params?.take) query.set('take', params.take.toString());
  const qs = query.toString();
  return apiFetch<AuditEventListResponse>(`/api/v1/admin/audit${qs ? '?' + qs : ''}`);
}
