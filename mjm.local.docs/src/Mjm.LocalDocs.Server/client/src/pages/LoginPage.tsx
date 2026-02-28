import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import Typography from '@mui/material/Typography';
import Alert from '@mui/material/Alert';
import FormControlLabel from '@mui/material/FormControlLabel';
import Checkbox from '@mui/material/Checkbox';
import InputAdornment from '@mui/material/InputAdornment';
import IconButton from '@mui/material/IconButton';
import CircularProgress from '@mui/material/CircularProgress';
import PersonOutlineRoundedIcon from '@mui/icons-material/PersonOutlineRounded';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import Visibility from '@mui/icons-material/Visibility';
import VisibilityOff from '@mui/icons-material/VisibilityOff';
import ArrowForwardRoundedIcon from '@mui/icons-material/ArrowForwardRounded';
import { alpha } from '@mui/material/styles';
import { useAuth } from '../context/AuthContext';

export default function LoginPage() {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const { login } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login(username, password, rememberMe);
      navigate('/');
    } catch {
      setError('Invalid username or password');
    } finally {
      setLoading(false);
    }
  };

  return (
    <Box
      sx={{
        width: '100%',
        maxWidth: 420,
        p: { xs: 3, sm: 5 },
        borderRadius: '24px',
        backdropFilter: 'blur(20px)',
        background: alpha('#fff', 0.07),
        border: `1px solid ${alpha('#fff', 0.12)}`,
        boxShadow: `0 25px 50px -12px rgba(0,0,0,0.4)`,
      }}
    >
      {/* Mobile logo */}
      <Box sx={{ display: { md: 'none' }, textAlign: 'center', mb: 3 }}>
        <Box
          sx={{
            width: 56,
            height: 56,
            borderRadius: '16px',
            background: 'linear-gradient(135deg, #3B82F6 0%, #8B5CF6 100%)',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 28,
            mb: 2,
          }}
        >
          &#128218;
        </Box>
      </Box>

      <Typography
        variant="h4"
        sx={{
          fontWeight: 800,
          color: '#fff',
          mb: 0.5,
        }}
      >
        Welcome back
      </Typography>
      <Typography
        sx={{
          color: alpha('#fff', 0.5),
          mb: 4,
          fontSize: '0.9375rem',
        }}
      >
        Sign in to your Local Docs account
      </Typography>

      {error && (
        <Alert
          severity="error"
          sx={{
            mb: 3,
            borderRadius: '12px',
            bgcolor: alpha('#EF4444', 0.12),
            color: '#FCA5A5',
            '& .MuiAlert-icon': { color: '#F87171' },
          }}
        >
          {error}
        </Alert>
      )}

      <Box component="form" onSubmit={handleSubmit}>
        <TextField
          fullWidth
          placeholder="Username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          required
          sx={{
            mb: 2,
            '& .MuiOutlinedInput-root': {
              borderRadius: '12px',
              bgcolor: alpha('#fff', 0.06),
              color: '#fff',
              '& fieldset': { borderColor: alpha('#fff', 0.12) },
              '&:hover fieldset': { borderColor: alpha('#fff', 0.25) },
              '&.Mui-focused fieldset': { borderColor: '#3B82F6' },
            },
            '& .MuiInputAdornment-root': { color: alpha('#fff', 0.4) },
          }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <PersonOutlineRoundedIcon />
                </InputAdornment>
              ),
            },
          }}
        />
        <TextField
          fullWidth
          placeholder="Password"
          type={showPassword ? 'text' : 'password'}
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          required
          sx={{
            mb: 1,
            '& .MuiOutlinedInput-root': {
              borderRadius: '12px',
              bgcolor: alpha('#fff', 0.06),
              color: '#fff',
              '& fieldset': { borderColor: alpha('#fff', 0.12) },
              '&:hover fieldset': { borderColor: alpha('#fff', 0.25) },
              '&.Mui-focused fieldset': { borderColor: '#3B82F6' },
            },
            '& .MuiInputAdornment-root': { color: alpha('#fff', 0.4) },
          }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <LockOutlinedIcon />
                </InputAdornment>
              ),
              endAdornment: (
                <InputAdornment position="end">
                  <IconButton
                    onClick={() => setShowPassword(!showPassword)}
                    edge="end"
                    sx={{ color: alpha('#fff', 0.4) }}
                  >
                    {showPassword ? <VisibilityOff /> : <Visibility />}
                  </IconButton>
                </InputAdornment>
              ),
            },
          }}
        />
        <FormControlLabel
          control={
            <Checkbox
              checked={rememberMe}
              onChange={(e) => setRememberMe(e.target.checked)}
              sx={{
                color: alpha('#fff', 0.3),
                '&.Mui-checked': { color: '#3B82F6' },
              }}
            />
          }
          label="Remember me"
          sx={{ color: alpha('#fff', 0.5), mt: 0.5, mb: 1 }}
        />
        <Button
          type="submit"
          fullWidth
          variant="contained"
          size="large"
          disabled={loading}
          endIcon={!loading && <ArrowForwardRoundedIcon />}
          sx={{
            mt: 2,
            py: 1.5,
            borderRadius: '12px',
            fontSize: '0.9375rem',
            background: 'linear-gradient(135deg, #3B82F6 0%, #8B5CF6 100%)',
            boxShadow: '0 8px 24px -4px rgba(59,130,246,0.4)',
            '&:hover': {
              background: 'linear-gradient(135deg, #2563EB 0%, #7C3AED 100%)',
              boxShadow: '0 12px 28px -4px rgba(59,130,246,0.5)',
            },
            '&.Mui-disabled': {
              background: alpha('#fff', 0.1),
              color: alpha('#fff', 0.3),
            },
          }}
        >
          {loading ? <CircularProgress size={24} sx={{ color: '#fff' }} /> : 'Sign In'}
        </Button>
      </Box>
    </Box>
  );
}
