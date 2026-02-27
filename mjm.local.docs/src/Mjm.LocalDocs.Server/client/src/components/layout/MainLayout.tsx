import { useState } from 'react';
import { Outlet, useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Drawer from '@mui/material/Drawer';
import IconButton from '@mui/material/IconButton';
import Typography from '@mui/material/Typography';
import Avatar from '@mui/material/Avatar';
import Tooltip from '@mui/material/Tooltip';
import { alpha } from '@mui/material/styles';
import MenuRoundedIcon from '@mui/icons-material/MenuRounded';
import ChevronLeftRoundedIcon from '@mui/icons-material/ChevronLeftRounded';
import LogoutRoundedIcon from '@mui/icons-material/LogoutRounded';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import { useAuth } from '../../context/AuthContext';
import NavMenu from './NavMenu';

const DRAWER_WIDTH = 260;
const COLLAPSED_WIDTH = 72;

export default function MainLayout() {
  const [drawerOpen, setDrawerOpen] = useState(true);
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login');
  };

  const currentWidth = drawerOpen ? DRAWER_WIDTH : COLLAPSED_WIDTH;

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      {/* Sidebar */}
      <Drawer
        variant="permanent"
        sx={{
          width: currentWidth,
          flexShrink: 0,
          transition: 'width 0.25s ease',
          '& .MuiDrawer-paper': {
            width: currentWidth,
            transition: 'width 0.25s ease',
            overflowX: 'hidden',
            bgcolor: '#fff',
            borderRight: (theme) => `1px solid ${alpha(theme.palette.divider, 0.5)}`,
          },
        }}
      >
        {/* Logo area */}
        <Box
          sx={{
            height: 64,
            display: 'flex',
            alignItems: 'center',
            px: drawerOpen ? 2.5 : 0,
            justifyContent: drawerOpen ? 'space-between' : 'center',
          }}
        >
          {drawerOpen ? (
            <>
              <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                <Box
                  sx={{
                    width: 36,
                    height: 36,
                    borderRadius: '10px',
                    background: 'linear-gradient(135deg, #3B82F6 0%, #8B5CF6 100%)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: 18,
                    flexShrink: 0,
                  }}
                >
                  &#128218;
                </Box>
                <Typography variant="h6" sx={{ fontWeight: 700, fontSize: '1.05rem' }} noWrap>
                  Local Docs
                </Typography>
              </Box>
              <IconButton
                size="small"
                onClick={() => setDrawerOpen(false)}
                sx={{
                  bgcolor: alpha('#94A3B8', 0.08),
                  '&:hover': { bgcolor: alpha('#94A3B8', 0.15) },
                }}
              >
                <ChevronLeftRoundedIcon fontSize="small" />
              </IconButton>
            </>
          ) : (
            <IconButton
              size="small"
              onClick={() => setDrawerOpen(true)}
              sx={{
                bgcolor: alpha('#94A3B8', 0.08),
                '&:hover': { bgcolor: alpha('#94A3B8', 0.15) },
              }}
            >
              <MenuRoundedIcon fontSize="small" />
            </IconButton>
          )}
        </Box>

        {/* Navigation */}
        <NavMenu collapsed={!drawerOpen} />

        {/* User section at bottom */}
        <Box sx={{ mt: 'auto', p: drawerOpen ? 2 : 1 }}>
          <Box
            sx={{
              display: 'flex',
              alignItems: 'center',
              gap: 1.5,
              p: drawerOpen ? 1.5 : 1,
              borderRadius: '12px',
              bgcolor: alpha('#94A3B8', 0.06),
              justifyContent: drawerOpen ? 'flex-start' : 'center',
            }}
          >
            <Avatar
              sx={{
                width: 34,
                height: 34,
                bgcolor: 'primary.main',
                fontSize: '0.875rem',
                fontWeight: 600,
              }}
            >
              {user?.username?.charAt(0).toUpperCase() || 'U'}
            </Avatar>
            {drawerOpen && (
              <>
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="body2" sx={{ fontWeight: 600, color: 'text.primary' }} noWrap>
                    {user?.username}
                  </Typography>
                  <Typography variant="caption" sx={{ color: 'text.secondary' }} noWrap>
                    Administrator
                  </Typography>
                </Box>
                <Tooltip title="Logout">
                  <IconButton
                    size="small"
                    onClick={handleLogout}
                    sx={{
                      color: 'text.secondary',
                      '&:hover': { color: 'error.main', bgcolor: alpha('#EF4444', 0.08) },
                    }}
                  >
                    <LogoutRoundedIcon fontSize="small" />
                  </IconButton>
                </Tooltip>
              </>
            )}
          </Box>
        </Box>
      </Drawer>

      {/* Main content area */}
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          display: 'flex',
          flexDirection: 'column',
          minWidth: 0,
        }}
      >
        {/* Top bar */}
        <Box
          sx={{
            height: 64,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'flex-end',
            px: 3,
            borderBottom: (theme) => `1px solid ${alpha(theme.palette.divider, 0.5)}`,
            bgcolor: alpha('#fff', 0.8),
            backdropFilter: 'blur(8px)',
            position: 'sticky',
            top: 0,
            zIndex: 10,
          }}
        >
          <Tooltip title="Search (coming soon)">
            <IconButton
              sx={{
                color: 'text.secondary',
                bgcolor: alpha('#94A3B8', 0.06),
                '&:hover': { bgcolor: alpha('#94A3B8', 0.12) },
              }}
            >
              <SearchRoundedIcon />
            </IconButton>
          </Tooltip>
        </Box>

        {/* Page content */}
        <Box sx={{ flex: 1, p: { xs: 2, sm: 3 }, maxWidth: 1400, width: '100%', mx: 'auto' }}>
          <Outlet />
        </Box>
      </Box>
    </Box>
  );
}
