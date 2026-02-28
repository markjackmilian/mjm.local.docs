import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemIcon from '@mui/material/ListItemIcon';
import ListItemText from '@mui/material/ListItemText';
import Typography from '@mui/material/Typography';
import Box from '@mui/material/Box';
import Divider from '@mui/material/Divider';
import { alpha } from '@mui/material/styles';
import GridViewRoundedIcon from '@mui/icons-material/GridViewRounded';
import FolderCopyRoundedIcon from '@mui/icons-material/FolderCopyRounded';
import HubRoundedIcon from '@mui/icons-material/HubRounded';
import KeyRoundedIcon from '@mui/icons-material/KeyRounded';
import { useNavigate, useLocation } from 'react-router-dom';

interface NavMenuProps {
  collapsed?: boolean;
}

const mainMenu = [
  { text: 'Dashboard', icon: <GridViewRoundedIcon />, path: '/' },
  { text: 'Projects', icon: <FolderCopyRoundedIcon />, path: '/projects' },
];

const settingsMenu = [
  { text: 'MCP Config', icon: <HubRoundedIcon />, path: '/mcp-config' },
  { text: 'API Tokens', icon: <KeyRoundedIcon />, path: '/settings/tokens' },
];

export default function NavMenu({ collapsed }: NavMenuProps) {
  const navigate = useNavigate();
  const location = useLocation();

  const isActive = (path: string) =>
    path === '/' ? location.pathname === '/' : location.pathname.startsWith(path);

  const renderItem = (item: { text: string; icon: React.ReactNode; path: string }) => (
    <ListItemButton
      key={item.path}
      selected={isActive(item.path)}
      onClick={() => navigate(item.path)}
      sx={{
        minHeight: 44,
        px: collapsed ? 2.5 : 2,
        justifyContent: collapsed ? 'center' : 'initial',
      }}
    >
      <ListItemIcon
        sx={{
          minWidth: collapsed ? 0 : 40,
          justifyContent: 'center',
          fontSize: 20,
          '& .MuiSvgIcon-root': { fontSize: 20 },
        }}
      >
        {item.icon}
      </ListItemIcon>
      {!collapsed && (
        <ListItemText
          primary={item.text}
          primaryTypographyProps={{ fontSize: '0.875rem', fontWeight: 500 }}
        />
      )}
    </ListItemButton>
  );

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', height: '100%', pt: 1 }}>
      {!collapsed && (
        <Typography
          variant="overline"
          sx={{ px: 3, pt: 2, pb: 1, color: alpha('#64748B', 0.7) }}
        >
          Main
        </Typography>
      )}
      <List disablePadding>{mainMenu.map(renderItem)}</List>

      <Divider sx={{ my: 1.5, mx: 2, borderColor: alpha('#94A3B8', 0.12) }} />

      {!collapsed && (
        <Typography
          variant="overline"
          sx={{ px: 3, pt: 1, pb: 1, color: alpha('#64748B', 0.7) }}
        >
          Settings
        </Typography>
      )}
      <List disablePadding>{settingsMenu.map(renderItem)}</List>
    </Box>
  );
}
