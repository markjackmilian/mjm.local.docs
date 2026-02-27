export interface Project {
  id: string;
  name: string;
  description: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface ProjectWithDocCount {
  project: Project;
  documentCount: number;
}

export interface Document {
  id: string;
  projectId: string;
  fileName: string;
  fileExtension: string;
  fileSizeBytes: number;
  versionNumber: number;
  parentDocumentId: string | null;
  isSuperseded: boolean;
  contentHash: string | null;
  createdAt: string;
  updatedAt: string | null;
}

export interface SearchResult {
  chunkId: string;
  content: string;
  documentId: string;
  fileName: string | null;
  score: number;
}

export interface ApiToken {
  id: string;
  name: string;
  tokenPrefix: string | null;
  createdAt: string;
  expiresAt: string | null;
  lastUsedAt: string | null;
  isRevoked: boolean;
  isValid: boolean;
}

export interface DashboardStats {
  projectCount: number;
  documentCount: number;
  totalSizeBytes: number;
  recentProjects: ProjectWithDocCount[];
}

export interface McpConfig {
  serverUrl: string;
  requireAuthentication: boolean;
  claudeCliCommand: string;
  claudeJsonConfig: string;
  openCodeJsonConfig: string;
}

export interface UserInfo {
  username: string;
  isAuthenticated: boolean;
}

export interface TokenCreated {
  token: ApiToken;
  plainTextToken: string;
}

export interface TokensResponse {
  tokens: ApiToken[];
  mcpAuthRequired: boolean;
}
