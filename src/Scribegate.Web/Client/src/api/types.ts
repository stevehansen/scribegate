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
  isAdmin: boolean;
  themePreference: 'light' | 'dark' | 'system';
  createdAt: string;
}

export interface RepositoryResponse {
  id: string;
  name: string;
  slug: string;
  owner: string;
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

// Proposals & Reviews

export interface ProposalSummary {
  id: string;
  title: string;
  status: string;
  documentPath?: string;
  createdBy: string;
  createdAt: string;
  reviewCount: number;
  commentCount: number;
}

export interface ProposalResponse {
  id: string;
  title: string;
  description?: string;
  status: string;
  proposedContent: string;
  proposedPath?: string;
  documentId?: string;
  documentPath?: string;
  baseRevisionId?: string;
  createdBy: string;
  createdAt: string;
  resolvedBy?: string;
  resolvedAt?: string;
  reviewCount: number;
  commentCount: number;
  diff?: DiffResult;
}

export interface ProposalListResponse {
  items: ProposalSummary[];
  total: number;
}

export interface DiffResult {
  lines: DiffLine[];
  hasChanges: boolean;
}

export interface DiffLine {
  type: string;
  text: string;
  position?: number;
}

export interface ReviewResponse {
  id: string;
  verdict: string;
  body?: string;
  createdBy: string;
  createdAt: string;
}

export interface ReviewListResponse {
  items: ReviewResponse[];
  total: number;
}

export interface CommentResponse {
  id: string;
  body: string;
  parentCommentId?: string;
  lineReference?: number;
  createdBy: string;
  createdById: string;
  createdAt: string;
}

export interface CommentListResponse {
  items: CommentResponse[];
  total: number;
}

export interface MemberResponse {
  userId: string;
  username: string;
  email: string;
  role: string;
}

export interface MemberListResponse {
  items: MemberResponse[];
  total: number;
}

export interface SettingResponse {
  key: string;
  value: string;
  updatedAt: string;
  group?: string;
  label?: string;
  type?: 'bool' | 'string' | 'number' | 'secret' | 'enum';
  description?: string;
  choices?: string[];
  defined?: boolean;
}

export interface AuditEventResponse {
  id: string;
  eventType: string;
  actorId?: string;
  actorUsername?: string;
  targetType: string;
  targetId?: string;
  details?: string;
  ipAddress?: string;
  createdAt: string;
}

export interface AuditEventListResponse {
  items: AuditEventResponse[];
  total: number;
}

export interface SignatureResponse {
  algorithm: string;
  publicKeyId: string;
  signature: string;
  contentHash: string;
  verified: boolean;
  createdAt: string;
}

// Share links

export interface ShareLinkResponse {
  id: string;
  tokenPrefix: string;
  description?: string;
  documentPath: string;
  revisionId?: string;
  createdBy: string;
  createdAt: string;
  expiresAt?: string;
  revokedAt?: string;
  lastAccessedAt?: string;
  accessCount: number;
  isActive: boolean;
}

export interface ShareLinkCreatedResponse {
  id: string;
  token: string;
  url: string;
  description?: string;
  createdAt: string;
  expiresAt?: string;
}

export interface ShareLinkListResponse {
  items: ShareLinkResponse[];
  total: number;
}

export interface PublicShareLinkResponse {
  repositorySlug: string;
  repositoryName: string;
  documentPath: string;
  content: string;
  revisionId: string;
  revisionMessage: string;
  revisionCreatedAt: string;
  expiresAt?: string;
}

// Webhooks

export interface WebhookResponse {
  id: string;
  url: string;
  description?: string;
  events: string[];
  enabled: boolean;
  consecutiveFailures: number;
  lastDeliveryAt?: string;
  lastDeliveryStatus?: number;
  disabledAt?: string;
  createdBy: string;
  createdAt: string;
  updatedAt?: string;
}

export interface WebhookCreatedResponse {
  id: string;
  url: string;
  description?: string;
  events: string[];
  enabled: boolean;
  secret: string;
  createdAt: string;
}

export interface WebhookListResponse {
  items: WebhookResponse[];
  total: number;
}

export interface WebhookDeliveryResponse {
  id: string;
  eventType: string;
  attemptCount: number;
  statusCode?: number;
  error?: string;
  succeeded: boolean;
  durationMs: number;
  createdAt: string;
  deliveredAt?: string;
}

export interface WebhookDeliveryListResponse {
  items: WebhookDeliveryResponse[];
  total: number;
}

// Templates

export interface TemplateSummaryResponse {
  id: string;
  name: string;
  description: string | null;
  createdBy: string;
  createdAt: string;
  updatedAt?: string;
}

export interface TemplateResponse extends TemplateSummaryResponse {
  content: string;
}

export interface TemplateListResponse {
  items: TemplateSummaryResponse[];
  total: number;
}

export interface CreateTemplateRequest {
  name: string;
  description?: string | null;
  content: string;
}

export type UpdateTemplateRequest = CreateTemplateRequest;
