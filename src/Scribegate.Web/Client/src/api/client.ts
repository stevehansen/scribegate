import type { ApiError } from './types.js';

export class ApiException extends Error {
  constructor(
    public readonly status: number,
    public readonly error: ApiError,
  ) {
    super(error.message);
    this.name = 'ApiException';
  }
}

function getToken(): string | null {
  return localStorage.getItem('sg_token');
}

export async function apiFetch<T>(
  path: string,
  options: RequestInit = {},
): Promise<T> {
  const headers: Record<string, string> = {
    ...options.headers as Record<string, string>,
  };

  const token = getToken();
  if (token) {
    headers['Authorization'] = `Bearer ${token}`;
  }

  if (options.body && typeof options.body === 'string') {
    headers['Content-Type'] = 'application/json';
  }

  const response = await fetch(path, {
    ...options,
    headers,
  });

  if (!response.ok) {
    let error: ApiError;
    try {
      const body = await response.json();
      error = body.error ?? {
        code: 'UNKNOWN',
        message: response.statusText,
      };
    } catch {
      error = {
        code: 'UNKNOWN',
        message: `HTTP ${response.status}: ${response.statusText}`,
      };
    }
    throw new ApiException(response.status, error);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return response.json();
}
