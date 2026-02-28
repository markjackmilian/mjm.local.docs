import { useState, useMemo } from 'react';
import { useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import TextField from '@mui/material/TextField';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Card from '@mui/material/Card';
import InputAdornment from '@mui/material/InputAdornment';
import { alpha } from '@mui/material/styles';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import FolderCopyRoundedIcon from '@mui/icons-material/FolderCopyRounded';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useSnackbar } from 'notistack';
import LoadingSpinner from '../../components/shared/LoadingSpinner';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import { useProjects, useDeleteProject } from '../../hooks/useProjects';
import { formatDate } from '../../utils/formatters';

export default function ProjectListPage() {
  const { data: projects, isLoading } = useProjects();
  const deleteProject = useDeleteProject();
  const navigate = useNavigate();
  const { enqueueSnackbar } = useSnackbar();
  const [search, setSearch] = useState('');
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const filtered = useMemo(() => {
    if (!projects) return [];
    if (!search) return projects;
    const lower = search.toLowerCase();
    return projects.filter(
      (p) =>
        p.project.name.toLowerCase().includes(lower) ||
        (p.project.description ?? '').toLowerCase().includes(lower),
    );
  }, [projects, search]);

  const handleDelete = async () => {
    if (!deleteId) return;
    try {
      await deleteProject.mutateAsync(deleteId);
      enqueueSnackbar('Project deleted', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to delete project', { variant: 'error' });
    }
    setDeleteId(null);
  };

  const columns: GridColDef[] = [
    {
      field: 'name',
      headerName: 'Name',
      flex: 1,
      minWidth: 200,
      renderCell: (params) => (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1.5,
            cursor: 'pointer',
            '&:hover': { color: 'primary.main' },
          }}
          onClick={() => navigate(`/projects/${params.row.id}`)}
        >
          <Box
            sx={{
              width: 32,
              height: 32,
              borderRadius: '8px',
              bgcolor: alpha('#3B82F6', 0.08),
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            <FolderCopyRoundedIcon sx={{ fontSize: 16, color: 'primary.main' }} />
          </Box>
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {params.value}
          </Typography>
        </Box>
      ),
    },
    {
      field: 'description',
      headerName: 'Description',
      flex: 1.5,
      minWidth: 200,
      renderCell: (params) => (
        <Typography variant="body2" color="text.secondary" noWrap>
          {params.value || 'No description'}
        </Typography>
      ),
    },
    {
      field: 'documentCount',
      headerName: 'Docs',
      width: 100,
      align: 'center',
      headerAlign: 'center',
      renderCell: (params) => (
        <Chip
          label={params.value}
          size="small"
          sx={{
            bgcolor: alpha('#3B82F6', 0.08),
            color: 'primary.main',
            fontWeight: 700,
            fontSize: '0.75rem',
          }}
        />
      ),
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
      field: 'actions',
      headerName: '',
      width: 100,
      sortable: false,
      renderCell: (params) => (
        <Box sx={{ display: 'flex', gap: 0.5 }}>
          <Tooltip title="Edit">
            <IconButton size="small" onClick={() => navigate(`/projects/${params.row.id}`)}>
              <EditRoundedIcon fontSize="small" />
            </IconButton>
          </Tooltip>
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

  const rows = filtered.map((p) => ({
    id: p.project.id,
    name: p.project.name,
    description: p.project.description ?? '',
    documentCount: p.documentCount,
    createdAt: p.project.createdAt,
  }));

  if (isLoading) return <LoadingSpinner />;

  return (
    <Box>
      <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', mb: 3 }}>
        <Box>
          <Typography variant="h4" sx={{ fontWeight: 800, mb: 0.5 }}>
            Projects
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {projects?.length ?? 0} total projects
          </Typography>
        </Box>
        <Button
          variant="contained"
          startIcon={<AddRoundedIcon />}
          onClick={() => navigate('/projects/new')}
          sx={{ px: 3 }}
        >
          New Project
        </Button>
      </Box>

      <TextField
        fullWidth
        placeholder="Search projects..."
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        size="small"
        sx={{ mb: 2.5 }}
        slotProps={{
          input: {
            startAdornment: (
              <InputAdornment position="start">
                <SearchRoundedIcon sx={{ color: 'text.secondary', fontSize: 20 }} />
              </InputAdornment>
            ),
          },
        }}
      />

      {rows.length === 0 ? (
        <Card
          sx={{
            p: 5,
            textAlign: 'center',
            border: '2px dashed',
            borderColor: alpha('#94A3B8', 0.2),
            bgcolor: 'transparent',
            boxShadow: 'none',
          }}
        >
          <FolderCopyRoundedIcon sx={{ fontSize: 48, color: alpha('#94A3B8', 0.3), mb: 2 }} />
          <Typography color="text.secondary">
            {search ? 'No projects match your search.' : 'No projects yet.'}
          </Typography>
        </Card>
      ) : (
        <Card sx={{ overflow: 'hidden' }}>
          <DataGrid
            rows={rows}
            columns={columns}
            autoHeight
            pageSizeOptions={[10, 25, 50]}
            initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
            disableRowSelectionOnClick
            sx={{
              border: 'none',
              '& .MuiDataGrid-row': { cursor: 'default' },
            }}
          />
        </Card>
      )}

      <ConfirmDialog
        open={!!deleteId}
        title="Delete Project"
        message="Are you sure you want to delete this project and all its documents? This action cannot be undone."
        confirmText="Delete"
        confirmColor="error"
        onConfirm={handleDelete}
        onCancel={() => setDeleteId(null)}
      />
    </Box>
  );
}
