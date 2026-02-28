import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Link from '@mui/material/Link';
import { alpha } from '@mui/material/styles';
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded';
import CreateNewFolderRoundedIcon from '@mui/icons-material/CreateNewFolderRounded';
import { useSnackbar } from 'notistack';
import { useCreateProject } from '../../hooks/useProjects';
import { existsByName } from '../../api/projects';

export default function ProjectNewPage() {
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [nameError, setNameError] = useState('');
  const createProject = useCreateProject();
  const navigate = useNavigate();
  const { enqueueSnackbar } = useSnackbar();

  const handleNameBlur = async () => {
    if (!name.trim()) return;
    const exists = await existsByName(name.trim());
    setNameError(exists ? 'A project with this name already exists' : '');
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (nameError) return;
    try {
      const project = await createProject.mutateAsync({
        name: name.trim(),
        description: description.trim() || undefined,
      });
      enqueueSnackbar('Project created', { variant: 'success' });
      navigate(`/projects/${project.id}`);
    } catch {
      enqueueSnackbar('Failed to create project', { variant: 'error' });
    }
  };

  return (
    <Box>
      <Breadcrumbs sx={{ mb: 3 }}>
        <Link
          underline="hover"
          color="inherit"
          sx={{ cursor: 'pointer', fontSize: '0.875rem' }}
          onClick={() => navigate('/')}
        >
          Dashboard
        </Link>
        <Link
          underline="hover"
          color="inherit"
          sx={{ cursor: 'pointer', fontSize: '0.875rem' }}
          onClick={() => navigate('/projects')}
        >
          Projects
        </Link>
        <Typography color="text.primary" fontSize="0.875rem" fontWeight={600}>
          New
        </Typography>
      </Breadcrumbs>

      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
        <Box
          sx={{
            width: 44,
            height: 44,
            borderRadius: '12px',
            bgcolor: alpha('#3B82F6', 0.08),
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <CreateNewFolderRoundedIcon sx={{ color: 'primary.main' }} />
        </Box>
        <Box>
          <Typography variant="h5" sx={{ fontWeight: 800 }}>
            New Project
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Create a new document collection
          </Typography>
        </Box>
      </Box>

      <Card sx={{ maxWidth: 600 }}>
        <CardContent sx={{ p: 3 }}>
          <Box component="form" onSubmit={handleSubmit}>
            <TextField
              fullWidth
              label="Project Name"
              value={name}
              onChange={(e) => {
                setName(e.target.value);
                setNameError('');
              }}
              onBlur={handleNameBlur}
              required
              error={!!nameError}
              helperText={nameError || 'Max 100 characters'}
              sx={{ mb: 2.5 }}
              slotProps={{ htmlInput: { maxLength: 100 } }}
            />
            <TextField
              fullWidth
              label="Description"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              multiline
              rows={3}
              helperText="Optional, max 500 characters"
              slotProps={{ htmlInput: { maxLength: 500 } }}
            />
            <Box sx={{ mt: 3, display: 'flex', gap: 1.5 }}>
              <Button
                type="submit"
                variant="contained"
                disabled={!name.trim() || !!nameError || createProject.isPending}
                sx={{ px: 3 }}
              >
                {createProject.isPending ? 'Creating...' : 'Create Project'}
              </Button>
              <Button
                variant="outlined"
                startIcon={<ArrowBackRoundedIcon />}
                onClick={() => navigate('/projects')}
                sx={{ color: 'text.secondary', borderColor: alpha('#94A3B8', 0.3) }}
              >
                Cancel
              </Button>
            </Box>
          </Box>
        </CardContent>
      </Card>
    </Box>
  );
}
