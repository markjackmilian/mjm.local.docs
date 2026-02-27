import Box from '@mui/material/Box';
import CircularProgress from '@mui/material/CircularProgress';
import Typography from '@mui/material/Typography';
import { alpha } from '@mui/material/styles';

export default function LoadingSpinner() {
  return (
    <Box
      display="flex"
      flexDirection="column"
      justifyContent="center"
      alignItems="center"
      minHeight="60vh"
      gap={2}
    >
      <CircularProgress
        size={44}
        thickness={3}
        sx={{
          color: 'primary.main',
          '& .MuiCircularProgress-circle': {
            strokeLinecap: 'round',
          },
        }}
      />
      <Typography variant="body2" sx={{ color: alpha('#64748B', 0.7), fontWeight: 500 }}>
        Loading...
      </Typography>
    </Box>
  );
}
