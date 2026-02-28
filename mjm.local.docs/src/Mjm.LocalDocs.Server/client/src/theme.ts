import { createTheme, alpha } from '@mui/material/styles';

const PRIMARY = '#3B82F6';
const SECONDARY = '#8B5CF6';
const BACKGROUND = '#F8FAFC';
const PAPER = '#FFFFFF';
const SURFACE = '#F1F5F9';

const theme = createTheme({
  palette: {
    primary: {
      main: PRIMARY,
      light: '#60A5FA',
      dark: '#2563EB',
      contrastText: '#fff',
    },
    secondary: {
      main: SECONDARY,
      light: '#A78BFA',
      dark: '#7C3AED',
    },
    success: {
      main: '#10B981',
      light: '#34D399',
      dark: '#059669',
    },
    warning: {
      main: '#F59E0B',
      light: '#FBBF24',
      dark: '#D97706',
    },
    error: {
      main: '#EF4444',
      light: '#F87171',
      dark: '#DC2626',
    },
    info: {
      main: '#06B6D4',
      light: '#22D3EE',
      dark: '#0891B2',
    },
    background: {
      default: BACKGROUND,
      paper: PAPER,
    },
    text: {
      primary: '#1E293B',
      secondary: '#64748B',
    },
    divider: alpha('#94A3B8', 0.2),
  },
  typography: {
    fontFamily: '"Inter", -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif',
    h1: { fontWeight: 800, letterSpacing: '-0.025em' },
    h2: { fontWeight: 700, letterSpacing: '-0.025em' },
    h3: { fontWeight: 700, letterSpacing: '-0.02em' },
    h4: { fontWeight: 700, letterSpacing: '-0.02em' },
    h5: { fontWeight: 600 },
    h6: { fontWeight: 600 },
    subtitle1: { fontWeight: 500, color: '#64748B' },
    subtitle2: { fontWeight: 500, color: '#64748B', fontSize: '0.8125rem' },
    body2: { color: '#64748B' },
    button: { fontWeight: 600, textTransform: 'none' as const },
    overline: { fontWeight: 700, letterSpacing: '0.08em', fontSize: '0.6875rem' },
  },
  shape: {
    borderRadius: 12,
  },
  shadows: [
    'none',
    `0 1px 2px 0 ${alpha('#1E293B', 0.05)}`,
    `0 1px 3px 0 ${alpha('#1E293B', 0.1)}, 0 1px 2px -1px ${alpha('#1E293B', 0.1)}`,
    `0 4px 6px -1px ${alpha('#1E293B', 0.1)}, 0 2px 4px -2px ${alpha('#1E293B', 0.1)}`,
    `0 10px 15px -3px ${alpha('#1E293B', 0.1)}, 0 4px 6px -4px ${alpha('#1E293B', 0.1)}`,
    `0 20px 25px -5px ${alpha('#1E293B', 0.1)}, 0 8px 10px -6px ${alpha('#1E293B', 0.1)}`,
    `0 25px 50px -12px ${alpha('#1E293B', 0.25)}`,
    // fill rest with same shadow
    ...Array(18).fill(`0 25px 50px -12px ${alpha('#1E293B', 0.25)}`),
  ] as any,
  components: {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          scrollbarWidth: 'thin',
          scrollbarColor: `${alpha('#94A3B8', 0.3)} transparent`,
          '&::-webkit-scrollbar': { width: 6, height: 6 },
          '&::-webkit-scrollbar-track': { background: 'transparent' },
          '&::-webkit-scrollbar-thumb': {
            background: alpha('#94A3B8', 0.3),
            borderRadius: 3,
          },
          '&::-webkit-scrollbar-thumb:hover': {
            background: alpha('#94A3B8', 0.5),
          },
        },
      },
    },
    MuiButton: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          padding: '8px 20px',
          fontWeight: 600,
          boxShadow: 'none',
          '&:hover': { boxShadow: 'none' },
        },
        contained: {
          background: `linear-gradient(135deg, ${PRIMARY} 0%, ${alpha(PRIMARY, 0.85)} 100%)`,
          '&:hover': {
            background: `linear-gradient(135deg, #2563EB 0%, ${PRIMARY} 100%)`,
          },
        },
        outlined: {
          borderWidth: 1.5,
          '&:hover': { borderWidth: 1.5 },
        },
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: {
          backgroundImage: 'none',
        },
        elevation1: {
          boxShadow: `0 1px 3px 0 ${alpha('#1E293B', 0.08)}, 0 1px 2px -1px ${alpha('#1E293B', 0.08)}`,
          border: `1px solid ${alpha('#94A3B8', 0.12)}`,
        },
      },
    },
    MuiCard: {
      styleOverrides: {
        root: {
          borderRadius: 16,
          border: `1px solid ${alpha('#94A3B8', 0.12)}`,
          boxShadow: `0 1px 3px 0 ${alpha('#1E293B', 0.08)}`,
          transition: 'all 0.2s ease-in-out',
          '&:hover': {
            boxShadow: `0 8px 25px -5px ${alpha('#1E293B', 0.12)}`,
            borderColor: alpha(PRIMARY, 0.3),
          },
        },
      },
    },
    MuiTextField: {
      styleOverrides: {
        root: {
          '& .MuiOutlinedInput-root': {
            borderRadius: 10,
            '& fieldset': {
              borderColor: alpha('#94A3B8', 0.25),
            },
            '&:hover fieldset': {
              borderColor: alpha(PRIMARY, 0.5),
            },
          },
        },
      },
    },
    MuiChip: {
      styleOverrides: {
        root: {
          fontWeight: 600,
          borderRadius: 8,
        },
        filled: {
          border: 'none',
        },
      },
    },
    MuiTableHead: {
      styleOverrides: {
        root: {
          '& .MuiTableCell-head': {
            fontWeight: 700,
            fontSize: '0.75rem',
            textTransform: 'uppercase',
            letterSpacing: '0.05em',
            color: '#64748B',
            backgroundColor: SURFACE,
          },
        },
      },
    },
    MuiDialog: {
      styleOverrides: {
        paper: {
          borderRadius: 16,
          boxShadow: `0 25px 50px -12px ${alpha('#1E293B', 0.25)}`,
        },
      },
    },
    MuiDrawer: {
      styleOverrides: {
        paper: {
          border: 'none',
        },
      },
    },
    MuiAppBar: {
      styleOverrides: {
        root: {
          boxShadow: 'none',
          backdropFilter: 'blur(8px)',
          borderBottom: `1px solid ${alpha('#94A3B8', 0.12)}`,
        },
      },
    },
    MuiListItemButton: {
      styleOverrides: {
        root: {
          borderRadius: 10,
          margin: '2px 8px',
          '&.Mui-selected': {
            backgroundColor: alpha(PRIMARY, 0.08),
            color: PRIMARY,
            '&:hover': {
              backgroundColor: alpha(PRIMARY, 0.12),
            },
            '& .MuiListItemIcon-root': {
              color: PRIMARY,
            },
          },
          '&:hover': {
            backgroundColor: alpha(PRIMARY, 0.04),
          },
        },
      },
    },
    MuiAlert: {
      styleOverrides: {
        root: {
          borderRadius: 12,
        },
        standardInfo: {
          backgroundColor: alpha('#06B6D4', 0.08),
          color: '#0E7490',
        },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: {
          borderRadius: 8,
          fontSize: '0.75rem',
          fontWeight: 500,
        },
      },
    },
  },
});

export default theme;
