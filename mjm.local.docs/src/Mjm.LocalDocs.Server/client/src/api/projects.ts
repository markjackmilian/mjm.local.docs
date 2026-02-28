import apiClient from './client';
import type { Project, ProjectWithDocCount } from '../types';

export async function getProjects(): Promise<ProjectWithDocCount[]> {
  const { data } = await apiClient.get<ProjectWithDocCount[]>('/projects');
  return data;
}

export async function getProject(id: string): Promise<Project> {
  const { data } = await apiClient.get<Project>(`/projects/${id}`);
  return data;
}

export async function createProject(name: string, description?: string): Promise<Project> {
  const { data } = await apiClient.post<Project>('/projects', { name, description });
  return data;
}

export async function updateProject(id: string, name: string, description?: string): Promise<Project> {
  const { data } = await apiClient.put<Project>(`/projects/${id}`, { name, description });
  return data;
}

export async function deleteProject(id: string): Promise<void> {
  await apiClient.delete(`/projects/${id}`);
}

export async function existsByName(name: string): Promise<boolean> {
  const { data } = await apiClient.get<{ exists: boolean }>('/projects/exists-by-name', { params: { name } });
  return data.exists;
}
