import { apiFetch } from './client.js';

export interface InstanceInfo {
  name: string;
  version: string;
  sourceUrl: string;
  product: string;
  tagline: string;
}

export function getInstanceInfo() {
  return apiFetch<InstanceInfo>('/api/v1/info');
}
