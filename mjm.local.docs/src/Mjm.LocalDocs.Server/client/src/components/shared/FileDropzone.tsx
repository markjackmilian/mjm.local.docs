import { useCallback } from 'react';
import { useDropzone } from 'react-dropzone';
import Box from '@mui/material/Box';
import Typography from '@mui/material/Typography';
import CircularProgress from '@mui/material/CircularProgress';
import { alpha } from '@mui/material/styles';
import CloudUploadRoundedIcon from '@mui/icons-material/CloudUploadRounded';
import { ACCEPTED_FILE_TYPES, MAX_FILE_SIZE } from '../../utils/fileHelpers';

interface FileDropzoneProps {
  onFilesSelected: (files: File[]) => void;
  maxFiles?: number;
  uploading?: boolean;
  accept?: Record<string, string[]>;
}

export default function FileDropzone({
  onFilesSelected,
  maxFiles = 10,
  uploading = false,
  accept = ACCEPTED_FILE_TYPES,
}: FileDropzoneProps) {
  const onDrop = useCallback(
    (acceptedFiles: File[]) => {
      onFilesSelected(acceptedFiles);
    },
    [onFilesSelected],
  );

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept,
    maxFiles,
    maxSize: MAX_FILE_SIZE,
    disabled: uploading,
  });

  return (
    <Box
      {...getRootProps()}
      sx={{
        border: '2px dashed',
        borderColor: isDragActive ? 'primary.main' : alpha('#94A3B8', 0.25),
        borderRadius: '14px',
        p: 3,
        textAlign: 'center',
        cursor: uploading ? 'default' : 'pointer',
        bgcolor: isDragActive ? alpha('#3B82F6', 0.04) : 'transparent',
        transition: 'all 0.2s ease',
        '&:hover': !uploading
          ? {
              borderColor: alpha('#3B82F6', 0.5),
              bgcolor: alpha('#3B82F6', 0.02),
            }
          : {},
      }}
    >
      <input {...getInputProps()} />
      {uploading ? (
        <CircularProgress size={36} thickness={3} />
      ) : (
        <>
          <Box
            sx={{
              width: 48,
              height: 48,
              borderRadius: '12px',
              bgcolor: alpha('#3B82F6', 0.08),
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              mb: 1.5,
            }}
          >
            <CloudUploadRoundedIcon sx={{ fontSize: 24, color: 'primary.main' }} />
          </Box>
          <Typography variant="body2" sx={{ fontWeight: 600, color: 'text.primary', mb: 0.5 }}>
            {isDragActive ? 'Drop files here...' : 'Drag files here or click to browse'}
          </Typography>
          <Typography variant="caption" sx={{ color: 'text.secondary' }}>
            .txt, .md, .pdf, .docx, .html, .json, .xml, .csv &middot; max 50 MB
          </Typography>
        </>
      )}
    </Box>
  );
}
