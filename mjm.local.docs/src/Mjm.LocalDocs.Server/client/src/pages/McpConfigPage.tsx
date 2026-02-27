import { useState } from 'react';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Tabs from '@mui/material/Tabs';
import Tab from '@mui/material/Tab';
import Alert from '@mui/material/Alert';
import IconButton from '@mui/material/IconButton';
import Tooltip from '@mui/material/Tooltip';
import Chip from '@mui/material/Chip';
import { alpha } from '@mui/material/styles';
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded';
import HubRoundedIcon from '@mui/icons-material/HubRounded';
import TerminalRoundedIcon from '@mui/icons-material/TerminalRounded';
import DataObjectRoundedIcon from '@mui/icons-material/DataObjectRounded';
import { useSnackbar } from 'notistack';
import { useQuery } from '@tanstack/react-query';
import { getMcpConfig } from '../api/mcpConfig';
import LoadingSpinner from '../components/shared/LoadingSpinner';

function CodeBlock({ code, onCopy }: { code: string; onCopy: () => void }) {
  return (
    <Box
      sx={{
        position: 'relative',
        bgcolor: '#1E293B',
        borderRadius: '12px',
        p: 2.5,
        pr: 6,
        overflow: 'auto',
      }}
    >
      <pre
        style={{
          margin: 0,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all',
          fontFamily: '"JetBrains Mono", "Fira Code", monospace',
          fontSize: 13,
          lineHeight: 1.6,
          color: '#E2E8F0',
        }}
      >
        {code}
      </pre>
      <Tooltip title="Copy">
        <IconButton
          size="small"
          onClick={onCopy}
          sx={{
            position: 'absolute',
            top: 10,
            right: 10,
            color: alpha('#fff', 0.5),
            bgcolor: alpha('#fff', 0.08),
            '&:hover': { bgcolor: alpha('#fff', 0.15), color: '#fff' },
          }}
        >
          <ContentCopyRoundedIcon fontSize="small" />
        </IconButton>
      </Tooltip>
    </Box>
  );
}

const tools = [
  ['search_docs', 'Search documents using semantic search'],
  ['list_projects', 'List all available projects'],
  ['get_project', 'Get details about a specific project'],
  ['create_project', 'Create a new project'],
  ['delete_project', 'Delete a project and all its documents'],
  ['add_document', 'Add a document to a project'],
  ['get_document', 'Get details about a specific document'],
  ['get_document_content', 'Get the full extracted text of a document'],
  ['update_document', 'Update a document with new content'],
  ['delete_document', 'Delete a document'],
  ['list_documents', 'List all documents in a project'],
];

export default function McpConfigPage() {
  const { data: config, isLoading } = useQuery({ queryKey: ['mcp-config'], queryFn: getMcpConfig });
  const [tab, setTab] = useState(0);
  const { enqueueSnackbar } = useSnackbar();

  const copy = (text: string) => {
    navigator.clipboard.writeText(text);
    enqueueSnackbar('Copied to clipboard', { variant: 'success' });
  };

  if (isLoading) return <LoadingSpinner />;
  if (!config) return null;

  return (
    <Box>
      {/* Header */}
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 1 }}>
        <Box
          sx={{
            width: 44,
            height: 44,
            borderRadius: '12px',
            bgcolor: alpha('#8B5CF6', 0.08),
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <HubRoundedIcon sx={{ color: '#8B5CF6' }} />
        </Box>
        <Box>
          <Typography variant="h4" sx={{ fontWeight: 800 }}>
            MCP Configuration
          </Typography>
          <Typography variant="body2" color="text.secondary">
            Configure AI tools to use Local Docs as an MCP server
          </Typography>
        </Box>
      </Box>

      {config.requireAuthentication && (
        <Alert
          severity="warning"
          sx={{
            my: 2.5,
            borderRadius: '12px',
            '& a': { color: 'inherit', fontWeight: 600 },
          }}
        >
          MCP authentication is enabled. You need an API token.{' '}
          <a href="/settings/tokens">Create one here</a>.
        </Alert>
      )}

      {/* Config Card */}
      <Card sx={{ mb: 4 }}>
        <Tabs
          value={tab}
          onChange={(_, v) => setTab(v)}
          sx={{
            borderBottom: (theme) => `1px solid ${theme.palette.divider}`,
            px: 2,
            '& .MuiTab-root': {
              textTransform: 'none',
              fontWeight: 600,
              fontSize: '0.875rem',
              minHeight: 48,
            },
          }}
        >
          <Tab icon={<TerminalRoundedIcon sx={{ fontSize: 18 }} />} iconPosition="start" label="Claude Code" />
          <Tab icon={<DataObjectRoundedIcon sx={{ fontSize: 18 }} />} iconPosition="start" label="OpenCode" />
        </Tabs>
        <CardContent sx={{ p: 3 }}>
          {tab === 0 && (
            <Box sx={{ display: 'flex', flexDirection: 'column', gap: 3 }}>
              <Box>
                <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'text.primary', mb: 1.5 }}>
                  Option 1: CLI Command
                </Typography>
                <CodeBlock code={config.claudeCliCommand} onCopy={() => copy(config.claudeCliCommand)} />
              </Box>
              <Box>
                <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'text.primary', mb: 0.5 }}>
                  Option 2: JSON Configuration
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ mb: 1.5, display: 'block' }}>
                  Add to <code style={{ background: alpha('#94A3B8', 0.15), padding: '2px 6px', borderRadius: 4 }}>.mcp.json</code> or{' '}
                  <code style={{ background: alpha('#94A3B8', 0.15), padding: '2px 6px', borderRadius: 4 }}>~/.claude.json</code>
                </Typography>
                <CodeBlock code={config.claudeJsonConfig} onCopy={() => copy(config.claudeJsonConfig)} />
              </Box>
            </Box>
          )}
          {tab === 1 && (
            <Box>
              <Typography variant="subtitle2" sx={{ fontWeight: 700, color: 'text.primary', mb: 0.5 }}>
                JSON Configuration
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ mb: 1.5, display: 'block' }}>
                Add to <code style={{ background: alpha('#94A3B8', 0.15), padding: '2px 6px', borderRadius: 4 }}>opencode.json</code>
              </Typography>
              <CodeBlock code={config.openCodeJsonConfig} onCopy={() => copy(config.openCodeJsonConfig)} />
            </Box>
          )}
        </CardContent>
      </Card>

      {/* Tools */}
      <Typography variant="h6" sx={{ fontWeight: 700, mb: 2 }}>
        Available MCP Tools
      </Typography>
      <Card>
        <Box sx={{ p: 0 }}>
          {tools.map(([name, desc], i) => (
            <Box
              key={name}
              sx={{
                display: 'flex',
                alignItems: 'center',
                gap: 2,
                px: 3,
                py: 1.5,
                borderBottom: i < tools.length - 1 ? (theme) => `1px solid ${theme.palette.divider}` : 'none',
                '&:hover': { bgcolor: alpha('#3B82F6', 0.02) },
              }}
            >
              <Chip
                label={name}
                size="small"
                sx={{
                  fontFamily: '"JetBrains Mono", monospace',
                  fontSize: '0.75rem',
                  fontWeight: 600,
                  bgcolor: alpha('#8B5CF6', 0.08),
                  color: '#7C3AED',
                  minWidth: 180,
                }}
              />
              <Typography variant="body2" color="text.secondary">
                {desc}
              </Typography>
            </Box>
          ))}
        </Box>
      </Card>
    </Box>
  );
}
