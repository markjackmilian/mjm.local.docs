import apiClient from './client';
import type { TokensResponse, TokenCreated } from '../types';

export async function getTokens(): Promise<TokensResponse> {
  const { data } = await apiClient.get<TokensResponse>('/tokens');
  return data;
}

export async function createToken(name: string, expiresAt?: string): Promise<TokenCreated> {
  const { data } = await apiClient.post<TokenCreated>('/tokens', { name, expiresAt: expiresAt || null });
  return data;
}

export async function revokeToken(id: string): Promise<void> {
  await apiClient.post(`/tokens/${id}/revoke`);
}

export async function deleteToken(id: string): Promise<void> {
  await apiClient.delete(`/tokens/${id}`);
}
