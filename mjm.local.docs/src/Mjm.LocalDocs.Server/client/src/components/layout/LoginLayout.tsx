import { Outlet } from 'react-router-dom';
import Box from '@mui/material/Box';
import { alpha } from '@mui/material/styles';

export default function LoginLayout() {
  return (
    <Box
      sx={{
        minHeight: '100vh',
        display: 'flex',
        position: 'relative',
        background: 'linear-gradient(135deg, #0F172A 0%, #1E293B 50%, #0F172A 100%)',
        overflow: 'hidden',
      }}
    >
      {/* Animated gradient orbs */}
      <Box
        sx={{
          position: 'absolute',
          width: 600,
          height: 600,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(59,130,246,0.15) 0%, transparent 70%)',
          top: -200,
          right: -100,
          animation: 'pulse 8s ease-in-out infinite',
          '@keyframes pulse': {
            '0%, 100%': { transform: 'scale(1)', opacity: 0.6 },
            '50%': { transform: 'scale(1.1)', opacity: 1 },
          },
        }}
      />
      <Box
        sx={{
          position: 'absolute',
          width: 500,
          height: 500,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(139,92,246,0.12) 0%, transparent 70%)',
          bottom: -150,
          left: -100,
          animation: 'pulse2 10s ease-in-out infinite',
          '@keyframes pulse2': {
            '0%, 100%': { transform: 'scale(1)', opacity: 0.5 },
            '50%': { transform: 'scale(1.15)', opacity: 0.9 },
          },
        }}
      />
      <Box
        sx={{
          position: 'absolute',
          width: 300,
          height: 300,
          borderRadius: '50%',
          background: 'radial-gradient(circle, rgba(6,182,212,0.1) 0%, transparent 70%)',
          top: '40%',
          left: '30%',
          animation: 'pulse3 12s ease-in-out infinite',
          '@keyframes pulse3': {
            '0%, 100%': { transform: 'scale(1)' },
            '50%': { transform: 'scale(1.2)' },
          },
        }}
      />

      {/* Left branding panel */}
      <Box
        sx={{
          flex: 1,
          display: { xs: 'none', md: 'flex' },
          alignItems: 'center',
          justifyContent: 'center',
          position: 'relative',
          zIndex: 1,
        }}
      >
        <Box sx={{ textAlign: 'center', p: 6, maxWidth: 480 }}>
          <Box
            sx={{
              width: 80,
              height: 80,
              borderRadius: '20px',
              background: 'linear-gradient(135deg, #3B82F6 0%, #8B5CF6 100%)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              fontSize: 40,
              mx: 'auto',
              mb: 4,
              boxShadow: '0 20px 40px -12px rgba(59,130,246,0.4)',
            }}
          >
            &#128218;
          </Box>
          <Box
            sx={{
              typography: 'h2',
              fontWeight: 800,
              color: '#fff',
              mb: 2,
              background: 'linear-gradient(135deg, #fff 0%, rgba(255,255,255,0.7) 100%)',
              backgroundClip: 'text',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
            }}
          >
            Local Docs
          </Box>
          <Box
            sx={{
              typography: 'h6',
              color: alpha('#fff', 0.6),
              fontWeight: 400,
              lineHeight: 1.6,
            }}
          >
            Document Management &<br />Semantic Search Platform
          </Box>
          {/* Feature pills */}
          <Box sx={{ display: 'flex', gap: 1.5, justifyContent: 'center', mt: 5, flexWrap: 'wrap' }}>
            {['MCP Server', 'AI Embeddings', 'Semantic Search'].map((label) => (
              <Box
                key={label}
                sx={{
                  px: 2,
                  py: 0.75,
                  borderRadius: '20px',
                  border: `1px solid ${alpha('#fff', 0.15)}`,
                  color: alpha('#fff', 0.7),
                  fontSize: '0.8125rem',
                  fontWeight: 500,
                  backdropFilter: 'blur(10px)',
                  background: alpha('#fff', 0.05),
                }}
              >
                {label}
              </Box>
            ))}
          </Box>
        </Box>
      </Box>

      {/* Right form panel */}
      <Box
        sx={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          p: { xs: 3, sm: 4 },
          zIndex: 1,
        }}
      >
        <Outlet />
      </Box>
    </Box>
  );
}
