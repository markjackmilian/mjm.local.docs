import { useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Chip from '@mui/material/Chip';
import IconButton from '@mui/material/IconButton';
import { alpha } from '@mui/material/styles';
import FolderCopyRoundedIcon from '@mui/icons-material/FolderCopyRounded';
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded';
import CloudRoundedIcon from '@mui/icons-material/CloudRounded';
import AddRoundedIcon from '@mui/icons-material/AddRounded';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import EastRoundedIcon from '@mui/icons-material/EastRounded';
import LoadingSpinner from '../components/shared/LoadingSpinner';
import { useDashboardStats } from '../hooks/useDashboard';
import { formatFileSize } from '../utils/formatters';

const statCards = [
  {
    key: 'projects',
    label: 'Total Projects',
    icon: FolderCopyRoundedIcon,
    gradient: 'linear-gradient(135deg, #3B82F6 0%, #2563EB 100%)',
    shadowColor: 'rgba(59,130,246,0.3)',
  },
  {
    key: 'documents',
    label: 'Total Documents',
    icon: DescriptionRoundedIcon,
    gradient: 'linear-gradient(135deg, #8B5CF6 0%, #7C3AED 100%)',
    shadowColor: 'rgba(139,92,246,0.3)',
  },
  {
    key: 'size',
    label: 'Storage Used',
    icon: CloudRoundedIcon,
    gradient: 'linear-gradient(135deg, #06B6D4 0%, #0891B2 100%)',
    shadowColor: 'rgba(6,182,212,0.3)',
  },
];

export default function DashboardPage() {
  const { data: stats, isLoading } = useDashboardStats();
  const navigate = useNavigate();

  if (isLoading) return <LoadingSpinner />;

  const statValues: Record<string, string | number> = {
    projects: stats?.projectCount ?? 0,
    documents: stats?.documentCount ?? 0,
    size: formatFileSize(stats?.totalSizeBytes ?? 0),
  };

  return (
    <Box>
      {/* Header */}
      <Box sx={{ mb: 4 }}>
        <Typography variant="h4" sx={{ fontWeight: 800, mb: 0.5 }}>
          Dashboard
        </Typography>
        <Typography variant="body1" sx={{ color: 'text.secondary' }}>
          Overview of your document management system
        </Typography>
      </Box>

      {/* Stats Cards */}
      <Grid container spacing={3} sx={{ mb: 5 }}>
        {statCards.map((card) => {
          const Icon = card.icon;
          return (
            <Grid size={{ xs: 12, sm: 4 }} key={card.key}>
              <Box
                sx={{
                  p: 3,
                  borderRadius: '16px',
                  background: card.gradient,
                  color: '#fff',
                  position: 'relative',
                  overflow: 'hidden',
                  boxShadow: `0 10px 30px -5px ${card.shadowColor}`,
                }}
              >
                {/* Background decoration */}
                <Box
                  sx={{
                    position: 'absolute',
                    top: -20,
                    right: -20,
                    width: 120,
                    height: 120,
                    borderRadius: '50%',
                    bgcolor: alpha('#fff', 0.1),
                  }}
                />
                <Box
                  sx={{
                    position: 'absolute',
                    bottom: -30,
                    right: 30,
                    width: 80,
                    height: 80,
                    borderRadius: '50%',
                    bgcolor: alpha('#fff', 0.06),
                  }}
                />
                <Box sx={{ position: 'relative', zIndex: 1 }}>
                  <Box
                    sx={{
                      width: 44,
                      height: 44,
                      borderRadius: '12px',
                      bgcolor: alpha('#fff', 0.2),
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      mb: 2,
                    }}
                  >
                    <Icon sx={{ fontSize: 24 }} />
                  </Box>
                  <Typography
                    variant="h3"
                    sx={{ fontWeight: 800, mb: 0.5, lineHeight: 1 }}
                  >
                    {statValues[card.key]}
                  </Typography>
                  <Typography
                    sx={{ fontSize: '0.875rem', opacity: 0.85, fontWeight: 500 }}
                  >
                    {card.label}
                  </Typography>
                </Box>
              </Box>
            </Grid>
          );
        })}
      </Grid>

      {/* Quick Actions */}
      <Box sx={{ mb: 5, display: 'flex', gap: 2 }}>
        <Button
          variant="contained"
          startIcon={<AddRoundedIcon />}
          onClick={() => navigate('/projects/new')}
          sx={{ px: 3 }}
        >
          New Project
        </Button>
        <Button
          variant="outlined"
          endIcon={<ArrowForwardRoundedIcon />}
          onClick={() => navigate('/projects')}
        >
          View All Projects
        </Button>
      </Box>

      {/* Recent Projects */}
      <Box sx={{ mb: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="h6" sx={{ fontWeight: 700 }}>
          Recent Projects
        </Typography>
        {(stats?.recentProjects.length ?? 0) > 0 && (
          <Button
            size="small"
            endIcon={<EastRoundedIcon />}
            onClick={() => navigate('/projects')}
            sx={{ color: 'text.secondary' }}
          >
            See all
          </Button>
        )}
      </Box>

      {!stats?.recentProjects.length ? (
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
          <FolderCopyRoundedIcon
            sx={{ fontSize: 48, color: alpha('#94A3B8', 0.3), mb: 2 }}
          />
          <Typography sx={{ color: 'text.secondary', mb: 2 }}>
            No projects yet. Create your first project to get started.
          </Typography>
          <Button
            variant="contained"
            startIcon={<AddRoundedIcon />}
            onClick={() => navigate('/projects/new')}
          >
            Create Project
          </Button>
        </Card>
      ) : (
        <Grid container spacing={2.5}>
          {stats.recentProjects.map(({ project, documentCount }) => (
            <Grid size={{ xs: 12, sm: 6, md: 4 }} key={project.id}>
              <Card
                sx={{
                  cursor: 'pointer',
                  height: '100%',
                  display: 'flex',
                  flexDirection: 'column',
                  '&:hover': {
                    transform: 'translateY(-2px)',
                  },
                }}
                onClick={() => navigate(`/projects/${project.id}`)}
              >
                <CardContent sx={{ flex: 1, p: 3 }}>
                  <Box
                    sx={{
                      display: 'flex',
                      alignItems: 'flex-start',
                      justifyContent: 'space-between',
                      mb: 1.5,
                    }}
                  >
                    <Box
                      sx={{
                        width: 40,
                        height: 40,
                        borderRadius: '10px',
                        bgcolor: alpha('#3B82F6', 0.08),
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                      }}
                    >
                      <FolderCopyRoundedIcon sx={{ color: 'primary.main', fontSize: 20 }} />
                    </Box>
                    <Chip
                      label={`${documentCount} docs`}
                      size="small"
                      sx={{
                        bgcolor: alpha('#3B82F6', 0.08),
                        color: 'primary.main',
                        fontWeight: 600,
                        fontSize: '0.75rem',
                      }}
                    />
                  </Box>
                  <Typography variant="subtitle1" sx={{ fontWeight: 700, color: 'text.primary', mb: 0.5 }} noWrap>
                    {project.name}
                  </Typography>
                  <Typography variant="body2" color="text.secondary" noWrap>
                    {project.description || 'No description'}
                  </Typography>
                </CardContent>
                <Box
                  sx={{
                    px: 3,
                    pb: 2.5,
                    display: 'flex',
                    justifyContent: 'flex-end',
                  }}
                >
                  <IconButton
                    size="small"
                    sx={{
                      bgcolor: alpha('#94A3B8', 0.08),
                      '&:hover': { bgcolor: alpha('#3B82F6', 0.1) },
                    }}
                  >
                    <EastRoundedIcon fontSize="small" />
                  </IconButton>
                </Box>
              </Card>
            </Grid>
          ))}
        </Grid>
      )}
    </Box>
  );
}
