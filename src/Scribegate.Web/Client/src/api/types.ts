// Mirrors the API response models from Scribegate.Web.Models

export interface ApiError {
  code: string;
  message: string;
  details?: string;
  field?: string;
  errors?: ApiFieldError[];
}

export interface ApiFieldError {
  field: string;
  code: string;
  message: string;
  details?: string;
}

export interface AuthResponse {
  token: string;
  expiresAt: string;
  user: UserInfo;
}

export interface UserInfo {
  id: string;
  username: string;
  email: string;
  createdAt: string;
}

export interface RepositoryResponse {
  id: string;
  name: string;
  slug: string;
  description?: string;
  visibility: string;
  createdAt: string;
  documentCount: number;
}

export interface RepositoryListResponse {
  items: RepositoryResponse[];
  total: number;
}

export interface DocumentResponse {
  id: string;
  path: string;
  content?: string;
  currentRevisionId?: string;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
}

export interface DocumentSummary {
  id: string;
  path: string;
  currentRevisionId?: string;
  createdAt: string;
  createdBy: string;
  updatedAt?: string;
}

export interface DocumentListResponse {
  items: DocumentSummary[];
  total: number;
}

export interface RevisionSummary {
  id: string;
  message: string;
  createdAt: string;
  createdBy: string;
  parentRevisionId?: string;
}

export interface RevisionResponse {
  id: string;
  content: string;
  message: string;
  createdAt: string;
  createdBy: string;
  parentRevisionId?: string;
}

export interface RevisionListResponse {
  items: RevisionSummary[];
  total: number;
}

export interface ApiTokenResponse {
  id: string;
  name: string;
  scopes?: string;
  createdAt: string;
  expiresAt?: string;
  lastUsedAt?: string;
}

export interface ApiTokenCreatedResponse {
  id: string;
  name: string;
  token: string;
  scopes?: string;
  createdAt: string;
  expiresAt?: string;
}
