import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  getProjectDocuments,
  uploadDocument,
  createKnowHow,
  updateDocumentVersion,
  updateKnowHow,
  deleteDocument,
  getDocumentVersions,
  getDocumentContent,
} from '../api/documents';

export function useProjectDocuments(projectId: string, includeSuperseded = false) {
  return useQuery({
    queryKey: ['projects', projectId, 'documents', { includeSuperseded }],
    queryFn: () => getProjectDocuments(projectId, includeSuperseded),
    enabled: !!projectId,
  });
}

export function useDocumentVersions(documentId: string) {
  return useQuery({
    queryKey: ['documents', documentId, 'versions'],
    queryFn: () => getDocumentVersions(documentId),
    enabled: !!documentId,
  });
}

export function useDocumentContent(documentId: string) {
  return useQuery({
    queryKey: ['documents', documentId, 'content'],
    queryFn: () => getDocumentContent(documentId),
    enabled: !!documentId,
  });
}

export function useUploadDocument(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => uploadDocument(projectId, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects', projectId, 'documents'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useCreateKnowHow(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ title, content }: { title: string; content: string }) =>
      createKnowHow(projectId, title, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects', projectId, 'documents'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateDocumentVersion() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, file }: { id: string; file: File }) => updateDocumentVersion(id, file),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      queryClient.invalidateQueries({ queryKey: ['documents'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateKnowHow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, title, content }: { id: string; title: string; content: string }) =>
      updateKnowHow(id, title, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      queryClient.invalidateQueries({ queryKey: ['documents'] });
    },
  });
}

export function useDeleteDocument() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteDocument(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      queryClient.invalidateQueries({ queryKey: ['documents'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}
