import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Button from '@mui/material/Button';
import { alpha } from '@mui/material/styles';
import HomeRoundedIcon from '@mui/icons-material/HomeRounded';
import { useNavigate } from 'react-router-dom';

export default function NotFoundPage() {
  const navigate = useNavigate();

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        minHeight: '70vh',
        textAlign: 'center',
      }}
    >
      <Typography
        sx={{
          fontSize: { xs: '6rem', sm: '8rem' },
          fontWeight: 900,
          lineHeight: 1,
          background: 'linear-gradient(135deg, #3B82F6 0%, #8B5CF6 100%)',
          backgroundClip: 'text',
          WebkitBackgroundClip: 'text',
          WebkitTextFillColor: 'transparent',
          mb: 1,
        }}
      >
        404
      </Typography>
      <Typography variant="h5" sx={{ fontWeight: 700, mb: 1 }}>
        Page Not Found
      </Typography>
      <Typography sx={{ color: alpha('#64748B', 0.8), mb: 4, maxWidth: 400 }}>
        The page you are looking for doesn't exist or has been moved.
      </Typography>
      <Button
        variant="contained"
        startIcon={<HomeRoundedIcon />}
        onClick={() => navigate('/')}
        sx={{ px: 3 }}
      >
        Back to Dashboard
      </Button>
    </Box>
  );
}
