import { useState, useMemo, useCallback } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import Box from '@mui/material/Box';
import Grid from '@mui/material/Grid';
import Card from '@mui/material/Card';
import CardContent from '@mui/material/CardContent';
import Typography from '@mui/material/Typography';
import TextField from '@mui/material/TextField';
import Button from '@mui/material/Button';
import IconButton from '@mui/material/IconButton';
import Chip from '@mui/material/Chip';
import Switch from '@mui/material/Switch';
import FormControlLabel from '@mui/material/FormControlLabel';
import Alert from '@mui/material/Alert';
import Breadcrumbs from '@mui/material/Breadcrumbs';
import Link from '@mui/material/Link';
import Tooltip from '@mui/material/Tooltip';
import InputAdornment from '@mui/material/InputAdornment';
import Dialog from '@mui/material/Dialog';
import DialogTitle from '@mui/material/DialogTitle';
import DialogContent from '@mui/material/DialogContent';
import DialogActions from '@mui/material/DialogActions';
import { alpha } from '@mui/material/styles';
import SearchRoundedIcon from '@mui/icons-material/SearchRounded';
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded';
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded';
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded';
import EditRoundedIcon from '@mui/icons-material/EditRounded';
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded';
import NoteAddRoundedIcon from '@mui/icons-material/NoteAddRounded';
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded';
import PictureAsPdfRoundedIcon from '@mui/icons-material/PictureAsPdfRounded';
import ArticleRoundedIcon from '@mui/icons-material/ArticleRounded';
import CodeRoundedIcon from '@mui/icons-material/CodeRounded';
import InsertDriveFileRoundedIcon from '@mui/icons-material/InsertDriveFileRounded';
import SaveRoundedIcon from '@mui/icons-material/SaveRounded';
import { DataGrid, type GridColDef } from '@mui/x-data-grid';
import { useSnackbar } from 'notistack';
import LoadingSpinner from '../../components/shared/LoadingSpinner';
import ConfirmDialog from '../../components/shared/ConfirmDialog';
import FileDropzone from '../../components/shared/FileDropzone';
import { useProject, useUpdateProject, useDeleteProject } from '../../hooks/useProjects';
import {
  useProjectDocuments,
  useUploadDocument,
  useCreateKnowHow,
  useUpdateDocumentVersion,
  useUpdateKnowHow,
  useDeleteDocument,
} from '../../hooks/useDocuments';
import { downloadDocumentFile, getDocumentVersions, getDocumentContent } from '../../api/documents';
import { existsByName } from '../../api/projects';
import { formatFileSize, formatDate } from '../../utils/formatters';
import { isTextDocument } from '../../utils/fileHelpers';
import type { Document } from '../../types';

function getFileIcon(ext: string) {
  const size = 16;
  switch (ext.toLowerCase()) {
    case '.pdf':
      return <PictureAsPdfRoundedIcon sx={{ fontSize: size, color: '#EF4444' }} />;
    case '.doc':
    case '.docx':
      return <ArticleRoundedIcon sx={{ fontSize: size, color: '#3B82F6' }} />;
    case '.md':
    case '.txt':
      return <DescriptionRoundedIcon sx={{ fontSize: size, color: '#06B6D4' }} />;
    case '.html':
    case '.htm':
    case '.json':
    case '.xml':
      return <CodeRoundedIcon sx={{ fontSize: size, color: '#8B5CF6' }} />;
    default:
      return <InsertDriveFileRoundedIcon sx={{ fontSize: size, color: '#94A3B8' }} />;
  }
}

export default function ProjectDetailPage() {
  const { projectId } = useParams<{ projectId: string }>();
  const navigate = useNavigate();
  const { enqueueSnackbar } = useSnackbar();

  const { data: project, isLoading: projectLoading } = useProject(projectId!);
  const [showSuperseded, setShowSuperseded] = useState(false);
  const { data: documents, isLoading: docsLoading } = useProjectDocuments(projectId!, showSuperseded);
  const updateProject = useUpdateProject();
  const deleteProjectMut = useDeleteProject();
  const uploadDoc = useUploadDocument(projectId!);
  const createKnowHow = useCreateKnowHow(projectId!);
  const updateDocVersion = useUpdateDocumentVersion();
  const updateKnowHowMut = useUpdateKnowHow();
  const deleteDocMut = useDeleteDocument();

  const [editName, setEditName] = useState('');
  const [editDesc, setEditDesc] = useState('');
  const [nameError, setNameError] = useState('');
  const [formInit, setFormInit] = useState(false);
  const [docSearch, setDocSearch] = useState('');
  const [deleteProjectOpen, setDeleteProjectOpen] = useState(false);
  const [deleteDocId, setDeleteDocId] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  // Know How dialogs
  const [knowHowOpen, setKnowHowOpen] = useState(false);
  const [knowHowTitle, setKnowHowTitle] = useState('');
  const [knowHowContent, setKnowHowContent] = useState('');
  const [editKnowHowOpen, setEditKnowHowOpen] = useState(false);
  const [editKnowHowDoc, setEditKnowHowDoc] = useState<Document | null>(null);
  const [editKnowHowTitle, setEditKnowHowTitle] = useState('');
  const [editKnowHowContent, setEditKnowHowContent] = useState('');

  // Upload version dialog
  const [updateVersionOpen, setUpdateVersionOpen] = useState(false);
  const [updateVersionDoc, setUpdateVersionDoc] = useState<Document | null>(null);
  const [updateVersionFile, setUpdateVersionFile] = useState<File | null>(null);

  // Version history dialog
  const [versionHistoryOpen, setVersionHistoryOpen] = useState(false);
  const [versionHistoryDocs, setVersionHistoryDocs] = useState<Document[]>([]);
  const [versionHistoryCurrentId, setVersionHistoryCurrentId] = useState('');

  if (project && !formInit) {
    setEditName(project.name);
    setEditDesc(project.description ?? '');
    setFormInit(true);
  }

  const filteredDocs = useMemo(() => {
    if (!documents) return [];
    if (!docSearch) return documents;
    const lower = docSearch.toLowerCase();
    return documents.filter((d) => d.fileName.toLowerCase().includes(lower));
  }, [documents, docSearch]);

  const activeDocCount = useMemo(() => documents?.filter((d) => !d.isSuperseded).length ?? 0, [documents]);
  const supersededDocCount = useMemo(() => documents?.filter((d) => d.isSuperseded).length ?? 0, [documents]);

  const handleSaveProject = async () => {
    if (!project || nameError) return;
    if (editName.trim() !== project.name) {
      const exists = await existsByName(editName.trim());
      if (exists) {
        setNameError('Name already exists');
        return;
      }
    }
    try {
      await updateProject.mutateAsync({
        id: project.id,
        name: editName.trim(),
        description: editDesc.trim() || undefined,
      });
      enqueueSnackbar('Project saved', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to save project', { variant: 'error' });
    }
  };

  const handleDeleteProject = async () => {
    if (!project) return;
    try {
      await deleteProjectMut.mutateAsync(project.id);
      enqueueSnackbar('Project deleted', { variant: 'success' });
      navigate('/projects');
    } catch {
      enqueueSnackbar('Failed to delete project', { variant: 'error' });
    }
    setDeleteProjectOpen(false);
  };

  const handleFilesSelected = useCallback(
    async (files: File[]) => {
      setUploading(true);
      for (const file of files) {
        try {
          await uploadDoc.mutateAsync(file);
          enqueueSnackbar(`Uploaded: ${file.name}`, { variant: 'success' });
        } catch {
          enqueueSnackbar(`Failed to upload: ${file.name}`, { variant: 'error' });
        }
      }
      setUploading(false);
    },
    [uploadDoc, enqueueSnackbar],
  );

  const handleCreateKnowHow = async () => {
    try {
      await createKnowHow.mutateAsync({ title: knowHowTitle.trim(), content: knowHowContent });
      enqueueSnackbar('Know How created', { variant: 'success' });
      setKnowHowOpen(false);
      setKnowHowTitle('');
      setKnowHowContent('');
    } catch {
      enqueueSnackbar('Failed to create Know How', { variant: 'error' });
    }
  };

  const openEditKnowHow = async (doc: Document) => {
    try {
      const content = await getDocumentContent(doc.id);
      setEditKnowHowDoc(doc);
      setEditKnowHowTitle(doc.fileName.replace(/\.[^/.]+$/, ''));
      setEditKnowHowContent(content);
      setEditKnowHowOpen(true);
    } catch {
      enqueueSnackbar('Failed to load document content', { variant: 'error' });
    }
  };

  const handleEditKnowHow = async () => {
    if (!editKnowHowDoc) return;
    try {
      await updateKnowHowMut.mutateAsync({
        id: editKnowHowDoc.id,
        title: editKnowHowTitle.trim(),
        content: editKnowHowContent,
      });
      enqueueSnackbar('Know How updated', { variant: 'success' });
      setEditKnowHowOpen(false);
    } catch {
      enqueueSnackbar('Failed to update Know How', { variant: 'error' });
    }
  };

  const handleUpdateVersion = async () => {
    if (!updateVersionDoc || !updateVersionFile) return;
    try {
      await updateDocVersion.mutateAsync({ id: updateVersionDoc.id, file: updateVersionFile });
      enqueueSnackbar('New version uploaded', { variant: 'success' });
      setUpdateVersionOpen(false);
      setUpdateVersionFile(null);
    } catch {
      enqueueSnackbar('Failed to upload new version', { variant: 'error' });
    }
  };

  const openVersionHistory = async (doc: Document) => {
    try {
      const versions = await getDocumentVersions(doc.id);
      setVersionHistoryDocs(versions);
      setVersionHistoryCurrentId(doc.id);
      setVersionHistoryOpen(true);
    } catch {
      enqueueSnackbar('Failed to load version history', { variant: 'error' });
    }
  };

  const handleDeleteDoc = async () => {
    if (!deleteDocId) return;
    try {
      await deleteDocMut.mutateAsync(deleteDocId);
      enqueueSnackbar('Document deleted', { variant: 'success' });
    } catch {
      enqueueSnackbar('Failed to delete document', { variant: 'error' });
    }
    setDeleteDocId(null);
  };

  const columns: GridColDef[] = [
    {
      field: 'fileName',
      headerName: 'File Name',
      flex: 1,
      minWidth: 200,
      renderCell: (params) => (
        <Box
          sx={{
            display: 'flex',
            alignItems: 'center',
            gap: 1.5,
            opacity: params.row.isSuperseded ? 0.5 : 1,
          }}
        >
          <Box
            sx={{
              width: 28,
              height: 28,
              borderRadius: '7px',
              bgcolor: alpha('#94A3B8', 0.08),
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              flexShrink: 0,
            }}
          >
            {getFileIcon(params.row.fileExtension)}
          </Box>
          <Typography variant="body2" sx={{ fontWeight: 500 }} noWrap>
            {params.value}
          </Typography>
        </Box>
      ),
    },
    {
      field: 'versionNumber',
      headerName: 'Ver.',
      width: 80,
      align: 'center',
      headerAlign: 'center',
      renderCell: (params) => (
        <Chip
          label={`v${params.value}`}
          size="small"
          sx={{
            fontWeight: 700,
            fontSize: '0.7rem',
            bgcolor: params.row.isSuperseded ? alpha('#94A3B8', 0.1) : alpha('#3B82F6', 0.08),
            color: params.row.isSuperseded ? '#94A3B8' : '#3B82F6',
          }}
        />
      ),
    },
    {
      field: 'fileExtension',
      headerName: 'Type',
      width: 70,
      renderCell: (params) => (
        <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
          {(params.value as string).replace('.', '').toUpperCase()}
        </Typography>
      ),
    },
    {
      field: 'fileSizeBytes',
      headerName: 'Size',
      width: 90,
      renderCell: (params) => (
        <Typography variant="caption" color="text.secondary">
          {formatFileSize(params.value as number)}
        </Typography>
      ),
    },
    {
      field: 'createdAt',
      headerName: 'Date',
      width: 130,
      renderCell: (params) => (
        <Typography variant="caption" color="text.secondary">
          {formatDate(params.value as string)}
        </Typography>
      ),
    },
    {
      field: 'actions',
      headerName: '',
      width: 180,
      sortable: false,
      renderCell: (params) => {
        const doc = params.row as Document;
        return (
          <Box sx={{ display: 'flex', gap: 0.25 }}>
            {doc.parentDocumentId && (
              <Tooltip title="Version History">
                <IconButton size="small" onClick={() => openVersionHistory(doc)}>
                  <HistoryRoundedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            )}
            {isTextDocument(doc.fileExtension) && !doc.isSuperseded && (
              <Tooltip title="Edit">
                <IconButton size="small" onClick={() => openEditKnowHow(doc)}>
                  <EditRoundedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            )}
            {!doc.isSuperseded && (
              <Tooltip title="New Version">
                <IconButton
                  size="small"
                  onClick={() => {
                    setUpdateVersionDoc(doc);
                    setUpdateVersionOpen(true);
                  }}
                >
                  <UploadFileRoundedIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            )}
            <Tooltip title="Download">
              <IconButton size="small" onClick={() => downloadDocumentFile(doc.id, doc.fileName)}>
                <DownloadRoundedIcon fontSize="small" />
              </IconButton>
            </Tooltip>
            <Tooltip title="Delete">
              <IconButton
                size="small"
                onClick={() => setDeleteDocId(doc.id)}
                sx={{ '&:hover': { color: 'error.main', bgcolor: alpha('#EF4444', 0.08) } }}
              >
                <DeleteOutlineRoundedIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        );
      },
    },
  ];

  if (projectLoading || docsLoading) return <LoadingSpinner />;
  if (!project) return <Alert severity="error">Project not found</Alert>;

  return (
    <Box>
      <Breadcrumbs sx={{ mb: 3 }}>
        <Link underline="hover" color="inherit" sx={{ cursor: 'pointer', fontSize: '0.875rem' }} onClick={() => navigate('/')}>
          Dashboard
        </Link>
        <Link underline="hover" color="inherit" sx={{ cursor: 'pointer', fontSize: '0.875rem' }} onClick={() => navigate('/projects')}>
          Projects
        </Link>
        <Typography color="text.primary" fontSize="0.875rem" fontWeight={600}>
          {project.name}
        </Typography>
      </Breadcrumbs>

      <Grid container spacing={3}>
        {/* Left Column - Project Info */}
        <Grid size={{ xs: 12, md: 4 }}>
          <Card>
            <CardContent sx={{ p: 3 }}>
              <Typography variant="h6" sx={{ fontWeight: 700, mb: 2.5 }}>
                Project Info
              </Typography>
              <TextField
                fullWidth
                label="Name"
                value={editName}
                onChange={(e) => {
                  setEditName(e.target.value);
                  setNameError('');
                }}
                error={!!nameError}
                helperText={nameError}
                size="small"
                sx={{ mb: 2 }}
                slotProps={{ htmlInput: { maxLength: 100 } }}
              />
              <TextField
                fullWidth
                label="Description"
                value={editDesc}
                onChange={(e) => setEditDesc(e.target.value)}
                multiline
                rows={3}
                size="small"
                slotProps={{ htmlInput: { maxLength: 500 } }}
              />
              <Box sx={{ mt: 2, display: 'flex', flexDirection: 'column', gap: 0.5 }}>
                <Typography variant="caption" color="text.secondary">
                  Created: {formatDate(project.createdAt)}
                </Typography>
                {project.updatedAt && (
                  <Typography variant="caption" color="text.secondary">
                    Updated: {formatDate(project.updatedAt)}
                  </Typography>
                )}
              </Box>
              <Box sx={{ mt: 2.5, display: 'flex', gap: 1.5 }}>
                <Button
                  variant="contained"
                  startIcon={<SaveRoundedIcon />}
                  onClick={handleSaveProject}
                  disabled={updateProject.isPending}
                  size="small"
                >
                  Save
                </Button>
                <Button
                  variant="outlined"
                  color="error"
                  startIcon={<DeleteOutlineRoundedIcon />}
                  onClick={() => setDeleteProjectOpen(true)}
                  size="small"
                  sx={{ borderColor: alpha('#EF4444', 0.3) }}
                >
                  Delete
                </Button>
              </Box>
            </CardContent>
          </Card>
        </Grid>

        {/* Right Column - Documents */}
        <Grid size={{ xs: 12, md: 8 }}>
          <Card>
            <CardContent sx={{ p: 3 }}>
              {/* Header */}
              <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2.5 }}>
                <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1 }}>
                  <Typography variant="h6" sx={{ fontWeight: 700 }}>
                    Documents
                  </Typography>
                  <Chip
                    label={activeDocCount}
                    size="small"
                    sx={{
                      height: 22,
                      fontWeight: 700,
                      fontSize: '0.7rem',
                      bgcolor: alpha('#3B82F6', 0.08),
                      color: 'primary.main',
                    }}
                  />
                  {supersededDocCount > 0 && (
                    <Typography variant="caption" color="text.secondary">
                      +{supersededDocCount} superseded
                    </Typography>
                  )}
                </Box>
                <FormControlLabel
                  control={
                    <Switch
                      checked={showSuperseded}
                      onChange={(e) => setShowSuperseded(e.target.checked)}
                      size="small"
                    />
                  }
                  label={<Typography variant="caption">Show old versions</Typography>}
                />
              </Box>

              {/* Upload Area */}
              <Grid container spacing={2} sx={{ mb: 2.5 }}>
                <Grid size={{ xs: 12, md: 7 }}>
                  <FileDropzone onFilesSelected={handleFilesSelected} uploading={uploading} />
                </Grid>
                <Grid size={{ xs: 12, md: 5 }}>
                  <Box
                    onClick={() => setKnowHowOpen(true)}
                    sx={{
                      border: '2px dashed',
                      borderColor: alpha('#8B5CF6', 0.25),
                      borderRadius: '14px',
                      p: 3,
                      textAlign: 'center',
                      cursor: 'pointer',
                      height: '100%',
                      display: 'flex',
                      flexDirection: 'column',
                      alignItems: 'center',
                      justifyContent: 'center',
                      '&:hover': {
                        borderColor: alpha('#8B5CF6', 0.5),
                        bgcolor: alpha('#8B5CF6', 0.02),
                      },
                      transition: 'all 0.2s ease',
                    }}
                  >
                    <Box
                      sx={{
                        width: 48,
                        height: 48,
                        borderRadius: '12px',
                        bgcolor: alpha('#8B5CF6', 0.08),
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        mb: 1.5,
                      }}
                    >
                      <NoteAddRoundedIcon sx={{ fontSize: 24, color: '#8B5CF6' }} />
                    </Box>
                    <Typography variant="body2" sx={{ fontWeight: 600, mb: 0.5 }}>
                      Add Know How
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      Create a Markdown document
                    </Typography>
                  </Box>
                </Grid>
              </Grid>

              {/* Search */}
              <TextField
                fullWidth
                placeholder="Search documents..."
                value={docSearch}
                onChange={(e) => setDocSearch(e.target.value)}
                size="small"
                sx={{ mb: 2 }}
                slotProps={{
                  input: {
                    startAdornment: (
                      <InputAdornment position="start">
                        <SearchRoundedIcon sx={{ color: 'text.secondary', fontSize: 20 }} />
                      </InputAdornment>
                    ),
                  },
                }}
              />

              {/* DataGrid */}
              {filteredDocs.length === 0 ? (
                <Box
                  sx={{
                    p: 4,
                    textAlign: 'center',
                    border: '2px dashed',
                    borderColor: alpha('#94A3B8', 0.15),
                    borderRadius: '12px',
                  }}
                >
                  <DescriptionRoundedIcon sx={{ fontSize: 40, color: alpha('#94A3B8', 0.3), mb: 1 }} />
                  <Typography variant="body2" color="text.secondary">
                    No documents yet. Upload files or create Know How to get started.
                  </Typography>
                </Box>
              ) : (
                <DataGrid
                  rows={filteredDocs}
                  columns={columns}
                  autoHeight
                  pageSizeOptions={[10, 25, 50]}
                  initialState={{ pagination: { paginationModel: { pageSize: 10 } } }}
                  disableRowSelectionOnClick
                  getRowClassName={(params) => (params.row.isSuperseded ? 'superseded-row' : '')}
                  sx={{
                    border: 'none',
                    '& .superseded-row': { opacity: 0.5 },
                  }}
                />
              )}
            </CardContent>
          </Card>
        </Grid>
      </Grid>

      {/* Delete Project Dialog */}
      <ConfirmDialog
        open={deleteProjectOpen}
        title="Delete Project"
        message="Are you sure you want to delete this project and ALL its documents? This cannot be undone."
        confirmText="Delete"
        confirmColor="error"
        onConfirm={handleDeleteProject}
        onCancel={() => setDeleteProjectOpen(false)}
      />

      {/* Delete Document Dialog */}
      <ConfirmDialog
        open={!!deleteDocId}
        title="Delete Document"
        message="Are you sure you want to delete this document? This cannot be undone."
        confirmText="Delete"
        confirmColor="error"
        onConfirm={handleDeleteDoc}
        onCancel={() => setDeleteDocId(null)}
      />

      {/* Add Know How Dialog */}
      <Dialog open={knowHowOpen} onClose={() => setKnowHowOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Add Know How</DialogTitle>
        <DialogContent>
          <TextField
            fullWidth
            label="Title"
            value={knowHowTitle}
            onChange={(e) => setKnowHowTitle(e.target.value)}
            required
            sx={{ mt: 1, mb: 2 }}
            slotProps={{ htmlInput: { maxLength: 200 } }}
          />
          <TextField
            fullWidth
            label="Content"
            value={knowHowContent}
            onChange={(e) => setKnowHowContent(e.target.value)}
            required
            multiline
            rows={15}
            placeholder="Write your content in Markdown..."
            slotProps={{ htmlInput: { maxLength: 500000 } }}
            sx={{
              '& .MuiOutlinedInput-root': {
                fontFamily: '"JetBrains Mono", monospace',
                fontSize: 13,
              },
            }}
          />
          <Alert severity="info" sx={{ mt: 2, borderRadius: '10px' }}>
            Content will be saved as Markdown (.md) and indexed for semantic search.
          </Alert>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={() => setKnowHowOpen(false)} sx={{ color: 'text.secondary' }}>
            Cancel
          </Button>
          <Button
            variant="contained"
            onClick={handleCreateKnowHow}
            disabled={!knowHowTitle.trim() || !knowHowContent.trim() || createKnowHow.isPending}
          >
            {createKnowHow.isPending ? 'Creating...' : 'Create'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Edit Know How Dialog */}
      <Dialog open={editKnowHowOpen} onClose={() => setEditKnowHowOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Edit Know How</DialogTitle>
        <DialogContent>
          <TextField
            fullWidth
            label="Title"
            value={editKnowHowTitle}
            onChange={(e) => setEditKnowHowTitle(e.target.value)}
            required
            sx={{ mt: 1, mb: 2 }}
            slotProps={{ htmlInput: { maxLength: 200 } }}
          />
          <TextField
            fullWidth
            label="Content"
            value={editKnowHowContent}
            onChange={(e) => setEditKnowHowContent(e.target.value)}
            required
            multiline
            rows={15}
            slotProps={{ htmlInput: { maxLength: 500000 } }}
            sx={{
              '& .MuiOutlinedInput-root': {
                fontFamily: '"JetBrains Mono", monospace',
                fontSize: 13,
              },
            }}
          />
          <Alert severity="info" sx={{ mt: 2, borderRadius: '10px' }}>
            A new version will be created. The current version will be preserved in history.
          </Alert>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button onClick={() => setEditKnowHowOpen(false)} sx={{ color: 'text.secondary' }}>
            Cancel
          </Button>
          <Button
            variant="contained"
            onClick={handleEditKnowHow}
            disabled={!editKnowHowTitle.trim() || !editKnowHowContent.trim() || updateKnowHowMut.isPending}
          >
            {updateKnowHowMut.isPending ? 'Saving...' : 'Save'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Upload New Version Dialog */}
      <Dialog
        open={updateVersionOpen}
        onClose={() => {
          setUpdateVersionOpen(false);
          setUpdateVersionFile(null);
        }}
        maxWidth="sm"
        fullWidth
      >
        <DialogTitle sx={{ fontWeight: 700 }}>Upload New Version</DialogTitle>
        <DialogContent>
          {updateVersionDoc && (
            <Box sx={{ mb: 2, display: 'flex', alignItems: 'center', gap: 1 }}>
              <Typography variant="body2" color="text.secondary">
                Current:
              </Typography>
              <Chip label={updateVersionDoc.fileName} size="small" />
              <Chip label={`v${updateVersionDoc.versionNumber}`} size="small" color="primary" />
            </Box>
          )}
          <FileDropzone onFilesSelected={(files) => setUpdateVersionFile(files[0] ?? null)} maxFiles={1} />
          {updateVersionFile && (
            <Chip
              label={`${updateVersionFile.name} (${formatFileSize(updateVersionFile.size)})`}
              sx={{ mt: 1.5 }}
              onDelete={() => setUpdateVersionFile(null)}
            />
          )}
          <Alert severity="info" sx={{ mt: 2, borderRadius: '10px' }}>
            The current version will be preserved in history.
          </Alert>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button
            onClick={() => {
              setUpdateVersionOpen(false);
              setUpdateVersionFile(null);
            }}
            sx={{ color: 'text.secondary' }}
          >
            Cancel
          </Button>
          <Button variant="contained" onClick={handleUpdateVersion} disabled={!updateVersionFile || updateDocVersion.isPending}>
            {updateDocVersion.isPending ? 'Uploading...' : 'Upload'}
          </Button>
        </DialogActions>
      </Dialog>

      {/* Version History Dialog */}
      <Dialog open={versionHistoryOpen} onClose={() => setVersionHistoryOpen(false)} maxWidth="md" fullWidth>
        <DialogTitle sx={{ fontWeight: 700 }}>Version History</DialogTitle>
        <DialogContent>
          <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
            {versionHistoryDocs.map((doc) => (
              <Box
                key={doc.id}
                sx={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 2,
                  p: 2,
                  borderRadius: '10px',
                  bgcolor: doc.id === versionHistoryCurrentId ? alpha('#3B82F6', 0.04) : 'transparent',
                  border: `1px solid ${doc.id === versionHistoryCurrentId ? alpha('#3B82F6', 0.2) : alpha('#94A3B8', 0.12)}`,
                  opacity: doc.isSuperseded ? 0.6 : 1,
                }}
              >
                <Chip
                  label={`v${doc.versionNumber}`}
                  size="small"
                  sx={{
                    fontWeight: 700,
                    bgcolor: doc.id === versionHistoryCurrentId ? alpha('#3B82F6', 0.1) : alpha('#94A3B8', 0.1),
                    color: doc.id === versionHistoryCurrentId ? '#3B82F6' : '#64748B',
                  }}
                />
                <Box sx={{ flex: 1, minWidth: 0 }}>
                  <Typography variant="body2" fontWeight={500} noWrap>
                    {doc.fileName}
                  </Typography>
                  <Typography variant="caption" color="text.secondary">
                    {formatDate(doc.createdAt)} &middot; {formatFileSize(doc.fileSizeBytes)}
                  </Typography>
                </Box>
                <Chip
                  label={doc.isSuperseded ? 'Superseded' : 'Current'}
                  size="small"
                  sx={{
                    fontWeight: 600,
                    fontSize: '0.7rem',
                    bgcolor: doc.isSuperseded ? alpha('#94A3B8', 0.1) : alpha('#10B981', 0.1),
                    color: doc.isSuperseded ? '#94A3B8' : '#059669',
                  }}
                />
              </Box>
            ))}
          </Box>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2.5 }}>
          <Button variant="contained" onClick={() => setVersionHistoryOpen(false)}>
            Close
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}
