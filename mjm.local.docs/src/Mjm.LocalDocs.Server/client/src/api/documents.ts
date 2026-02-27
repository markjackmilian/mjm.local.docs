import apiClient from './client';
import type { Document } from '../types';

export async function getProjectDocuments(projectId: string, includeSuperseded = false): Promise<Document[]> {
  const { data } = await apiClient.get<Document[]>(`/projects/${projectId}/documents`, {
    params: { includeSuperseded },
  });
  return data;
}

export async function uploadDocument(projectId: string, file: File): Promise<Document> {
  const formData = new FormData();
  formData.append('file', file);
  const { data } = await apiClient.post<Document>(`/projects/${projectId}/documents`, formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return data;
}

export async function createKnowHow(projectId: string, title: string, content: string): Promise<Document> {
  const { data } = await apiClient.post<Document>(`/projects/${projectId}/documents/knowhow`, { title, content });
  return data;
}

export async function getDocument(id: string): Promise<Document> {
  const { data } = await apiClient.get<Document>(`/documents/${id}`);
  return data;
}

export async function getDocumentContent(id: string): Promise<string> {
  const { data } = await apiClient.get<{ extractedText: string }>(`/documents/${id}/content`);
  return data.extractedText;
}

export async function downloadDocumentFile(id: string, fileName: string): Promise<void> {
  const response = await apiClient.get(`/documents/${id}/file`, { responseType: 'blob' });
  const url = window.URL.createObjectURL(new Blob([response.data]));
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', fileName);
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.URL.revokeObjectURL(url);
}

export async function updateDocumentVersion(id: string, file: File): Promise<Document> {
  const formData = new FormData();
  formData.append('file', file);
  const { data } = await apiClient.put<Document>(`/documents/${id}`, formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return data;
}

export async function updateKnowHow(id: string, title: string, content: string): Promise<Document> {
  const { data } = await apiClient.put<Document>(`/documents/${id}/knowhow`, { title, content });
  return data;
}

export async function deleteDocument(id: string): Promise<void> {
  await apiClient.delete(`/documents/${id}`);
}

export async function getDocumentVersions(id: string): Promise<Document[]> {
  const { data } = await apiClient.get<Document[]>(`/documents/${id}/versions`);
  return data;
}
