import apiClient from './client';
import type { McpConfig } from '../types';

export async function getMcpConfig(): Promise<McpConfig> {
  const { data } = await apiClient.get<McpConfig>('/mcp-config');
  return data;
}
