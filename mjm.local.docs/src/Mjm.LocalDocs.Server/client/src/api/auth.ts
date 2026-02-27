import apiClient from './client';
import type { UserInfo } from '../types';

export async function login(username: string, password: string, rememberMe: boolean): Promise<UserInfo> {
  const { data } = await apiClient.post<UserInfo>('/auth/login', { username, password, rememberMe });
  return data;
}

export async function logout(): Promise<void> {
  await apiClient.post('/auth/logout');
}

export async function getMe(): Promise<UserInfo> {
  const { data } = await apiClient.get<UserInfo>('/auth/me');
  return data;
}
