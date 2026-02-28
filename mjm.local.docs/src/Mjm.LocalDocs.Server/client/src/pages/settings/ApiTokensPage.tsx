import { useState } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import Alert from '@mui/material/Alert';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Card from '@mui/material/Card';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import TextField from '@mui/material/TextField';
import { alpha } from '@mui/material/styles';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import BlockRoundedIcon from '@mui/icons-material/BlockRounded';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useSnackbar } from 'notistack';
import LoadingSpinner from '../../components/shared/LoadingSpinner';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { useTokens, useCreateToken, useRevokeToken, useDeleteToken } from '../../hooks/useTokens';
import { formatDate } from '../../utils/formatters';

export default function ApiTokensPage() {
  const { data, isLoading } = useTokens();
  const createToken = useCreateToken();
  const revokeToken = useRevokeToken();
  const deleteToken = useDeleteToken();
  const { enqueueSnackbar } = useSnackbar();

  const [createOpen, setCreateOpen] = useState(false);
  const [tokenName, setTokenName] = useState('');
  const [createdToken, setCreatedToken] = useState<string | null>(null);
  const [revokeId, setRevokeId] = useState<string | null>(null);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const handleCreate = async () => {
    try {
      const result = await createToken.mutateAsync({ name: tokenName.trim() });
      setCreatedToken(result.plainTextToken);
      setTokenName('');
      setCreateOpen(false);
      enqueueSnackbar('Token created', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to create token', { variant: 'error' });
    }
  };

  const handleRevoke = async () => {
    if (!revokeId) return;
    try {
      await revokeToken.mutateAsync(revokeId);
      enqueueSnackbar('Token revoked', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to revoke token', { variant: 'error' });
    }
    setRevokeId(null);
  };

  const handleDelete = async () => {
    if (!deleteId) return;
    try {
      await deleteToken.mutateAsync(deleteId);
      enqueueSnackbar('Token deleted', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to delete token', { variant: 'error' });
    }
    setDeleteId(null);
  };

  const statusConfig: Record<string, { label: string; color: string; bg: string }> = {
    active: { label: 'Active', color: '#059669', bg: alpha('#10B981', 0.1) },
    expired: { label: 'Expired', color: '#D97706', bg: alpha('#F59E0B', 0.1) },
    revoked: { label: 'Revoked', color: '#DC2626', bg: alpha('#EF4444', 0.1) },
  };

  const getStatus = (row: { isRevoked: boolean; isValid: boolean }) => {
    if (row.isRevoked) return 'revoked';
    if (!row.isValid) return 'expired';
    return 'active';
  };

  const columns: GridColDef[] = [
    {
      field: 'name',
      headerName: 'Name',
      flex: 1,
      minWidth: 150,
      renderCell: (params) => (
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
          <Box
            sx={{
              width: 30,
              height: 30,
              borderRadius: '8px',
              bgcolor: alpha('#8B5CF6', 0.08),
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <KeyRoundedIcon sx={{ fontSize: 14, color: '#8B5CF6' }} />
          </Box>
          <Typography variant="body2" fontWeight={600}>
            {params.value}
          </Typography>
        </Box>
      ),
    },
    {
      field: 'tokenPrefix',
      headerName: 'Prefix',
      width: 130,
      renderCell: (params) => (
        <Typography
          variant="caption"
          sx={{
            fontFamily: 'monospace',
            bgcolor: alpha('#94A3B8', 0.08),
            px: 1,
            py: 0.3,
            borderRadius: '6px',
          }}
        >
          {params.value ? `${params.value}...` : '-'}
        </Typography>
      ),
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 110,
      renderCell: (params) => {
        const status = getStatus(params.row);
        const cfg = statusConfig[status];
        return (
          <Chip
            label={cfg.label}
            size="small"
            sx={{ bgcolor: cfg.bg, color: cfg.color, fontWeight: 700, fontSize: '0.7rem' }}
          />
        );
      },
    },
    {
      field: 'createdAt',
      headerName: 'Created',
      width: 140,
      renderCell: (params) => (
        <Typography variant="caption" color="text.secondary">
          {formatDate(params.value)}
        </Typography>
      ),
    },
    {
      field: 'lastUsedAt',
      headerName: 'Last Used',
      width: 140,
      renderCell: (params) => (
        <Typography variant="caption" color="text.secondary">
          {params.value ? formatDate(params.value) : 'Never'}
        </Typography>
      ),
    },
    {
      field: 'actions',
      headerName: '',
      width: 100,
      sortable: false,
      renderCell: (params) => (
        <Box sx={{ display: 'flex', gap: 0.5 }}>
          {params.row.isValid && !params.row.isRevoked && (
            <Tooltip title="Revoke">
              <IconButton
                size="small"
                onClick={() => setRevokeId(params.row.id)}
                sx={{ '&:hover': { color: 'warning.main', bgcolor: alpha('#F59E0B', 0.08) } }}
              >
                <BlockRoundedIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
          <Tooltip title="Delete">
            <IconButton
              size="small"
              onClick={() => setDeleteId(params.row.id)}
              sx={{ '&:hover': { color: 'error.main', bgcolor: alpha('#EF4444', 0.08) } }}
            >
              <DeleteOutlineRoundedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
        </Box>
      ),
    },
  ];

  if (isLoading) return <LoadingSpinner />;

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 3 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
          <Box
            sx={{
              width: 44,
              height: 44,
              borderRadius: '12px',
              bgcolor: alpha('#8B5CF6', 0.08),
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <KeyRoundedIcon sx={{ color: '#8B5CF6' }} />
          </Box>
          <Box>
            <Typography variant="h4" sx={{ fontWeight: 800 }}>
              API Tokens
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Manage authentication tokens for MCP access
            </Typography>
          </Box>
        </Box>
        <Button
          variant="contained"
          startIcon={<AddRoundedIcon />}
          onClick={() => setCreateOpen(true)}
          sx={{ px: 3 }}
        >
          New Token
        </Button>
      </Box>

      {data && !data.mcpAuthRequired && (
        <Alert severity="warning" sx={{ mb: 2.5, borderRadius: '12px' }}>
          MCP authentication is currently disabled. Tokens are not required for MCP access.
        </Alert>
      )}

      <Card sx={{ overflow: 'hidden' }}>
        <DataGrid
          rows={data?.tokens ?? []}
          columns={columns}
          autoHeight
          pageSizeOptions={[10, 25]}
          initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
          disableRowSelectionOnClick
          sx={{ border: 'none' }}
        />
      </Card>

      {/* Create Token Dialog */}
      <Dialog open={createOpen} onClose={() => setCreateOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Create API Token</DialogTitle>
        <DialogContent>
          <TextField
            fullWidth
            label="Token Name"
            value={tokenName}
            onChange={(e) => setTokenName(e.target.value)}
            required
            sx={{ mt: 1 }}
            placeholder="e.g., Claude Code"
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={() => setCreateOpen(false)} sx={{ color: 'text.secondary' }}>
            Cancel
          </Button>
          <Button
            variant="contained"
            onClick={handleCreate}
            disabled={!tokenName.trim() || createToken.isPending}
          >
            Create
          </Button>
        </DialogActions>
      </Dialog>

      {/* Show Created Token */}
      <Dialog open={!!createdToken} onClose={() => setCreatedToken(null)} maxWidth="sm" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Token Created</DialogTitle>
        <DialogContent>
          <Alert severity="warning" sx={{ mb: 2.5, borderRadius: '12px' }}>
            Copy this token now. You won't be able to see it again!
          </Alert>
          <Box
            sx={{
              bgcolor: '#1E293B',
              p: 2,
              borderRadius: '12px',
              fontFamily: 'monospace',
              fontSize: 13,
              color: '#E2E8F0',
              wordBreak: 'break-all',
              display: 'flex',
              alignItems: 'center',
              gap: 1,
            }}
          >
            <Box sx={{ flexGrow: 1 }}>{createdToken}</Box>
            <Tooltip title="Copy">
              <IconButton
                size="small"
                onClick={() => {
                  if (createdToken) navigator.clipboard.writeText(createdToken);
                  enqueueSnackbar('Token copied', { variant: 'success' });
                }}
                sx={{ color: alpha('#fff', 0.5), '&:hover': { color: '#fff' } }}
              >
                <ContentCopyRoundedIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button variant="contained" onClick={() => setCreatedToken(null)}>
            Done
          </Button>
        </DialogActions>
      </Dialog>

      <ConfirmDialog
        open={!!revokeId}
        title="Revoke Token"
        message="Are you sure? This token will no longer be valid for MCP authentication."
        confirmText="Revoke"
        confirmColor="warning"
        onConfirm={handleRevoke}
        onCancel={() => setRevokeId(null)}
      />

      <ConfirmDialog
        open={!!deleteId}
        title="Delete Token"
        message="Are you sure you want to permanently delete this token?"
        confirmText="Delete"
        confirmColor="error"
        onConfirm={handleDelete}
        onCancel={() => setDeleteId(null)}
      />
    </Box>
  );
}
