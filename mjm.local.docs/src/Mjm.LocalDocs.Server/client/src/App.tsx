import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ThemeProvider } from '@mui/material/styles';
import CssBaseline from '@mui/material/CssBaseline';
import { SnackbarProvider } from 'notistack';
import theme from './theme';
import { AuthProvider } from './context/AuthContext';
import ProtectedRoute from './components/shared/ProtectedRoute';
import MainLayout from './components/layout/MainLayout';
import LoginLayout from './components/layout/LoginLayout';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import ProjectListPage from './pages/projects/ProjectListPage';
import ProjectNewPage from './pages/projects/ProjectNewPage';
import ProjectDetailPage from './pages/projects/ProjectDetailPage';
import McpConfigPage from './pages/McpConfigPage';
import ApiTokensPage from './pages/settings/ApiTokensPage';
import NotFoundPage from './pages/NotFoundPage';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <SnackbarProvider maxSnack={3} autoHideDuration={4000}>
          <AuthProvider>
            <BrowserRouter>
              <Routes>
                {/* Login route with its own layout */}
                <Route element={<LoginLayout />}>
                  <Route path="/login" element={<LoginPage />} />
                </Route>

                {/* Protected routes with main layout */}
                <Route element={<ProtectedRoute />}>
                  <Route element={<MainLayout />}>
                    <Route path="/" element={<DashboardPage />} />
                    <Route path="/projects" element={<ProjectListPage />} />
                    <Route path="/projects/new" element={<ProjectNewPage />} />
                    <Route path="/projects/:projectId" element={<ProjectDetailPage />} />
                    <Route path="/mcp-config" element={<McpConfigPage />} />
                    <Route path="/settings/tokens" element={<ApiTokensPage />} />
                  </Route>
                </Route>

                {/* Catch-all */}
                <Route path="*" element={<NotFoundPage />} />
              </Routes>
            </BrowserRouter>
          </AuthProvider>
        </SnackbarProvider>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
