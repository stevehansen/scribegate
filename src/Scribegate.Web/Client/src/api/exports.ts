import { ApiException } from './client.js';
import type { ApiError } from './types.js';

async function downloadZip(owner: string, repoSlug: string, endpoint: string, defaultFileName: string): Promise<void> {
  const token = localStorage.getItem('sg_token');
  const headers: Record<string, string> = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;

  const response = await fetch(
    `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(repoSlug)}/${endpoint}`,
    { headers },
  );

  if (!response.ok) {
    let error: ApiError;
    try {
      const body = await response.json();
      error = body.error ?? { code: 'UNKNOWN', message: response.statusText };
    } catch {
      error = { code: 'UNKNOWN', message: `HTTP ${response.status}: ${response.statusText}` };
    }
    throw new ApiException(response.status, error);
  }

  const blob = await response.blob();
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename="?([^"]+)"?/i.exec(disposition);
  const fileName = match?.[1] ?? defaultFileName;

  const url = URL.createObjectURL(blob);
  try {
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

export async function downloadRepoZip(owner: string, repoSlug: string): Promise<void> {
  return downloadZip(owner, repoSlug, 'export', `${repoSlug}.zip`);
}

export async function buildSite(owner: string, repoSlug: string): Promise<void> {
  return downloadZip(owner, repoSlug, 'site', `${repoSlug}-site.zip`);
}
