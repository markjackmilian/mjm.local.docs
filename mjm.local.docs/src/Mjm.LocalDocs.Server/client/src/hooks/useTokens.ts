import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getTokens, createToken, revokeToken, deleteToken } from '../api/tokens';

export function useTokens() {
  return useQuery({ queryKey: ['tokens'], queryFn: getTokens });
}

export function useCreateToken() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ name, expiresAt }: { name: string; expiresAt?: string }) =>
      createToken(name, expiresAt),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tokens'] });
    },
  });
}

export function useRevokeToken() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => revokeToken(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tokens'] });
    },
  });
}

export function useDeleteToken() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => deleteToken(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['tokens'] });
    },
  });
}
