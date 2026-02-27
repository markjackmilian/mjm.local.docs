import apiClient from './client';
import type { SearchResult } from '../types';

export async function search(query: string, projectId?: string, limit = 5): Promise<SearchResult[]> {
  const { data } = await apiClient.post<SearchResult[]>('/search', { query, projectId, limit });
  return data;
}
