export function getFileIconName(extension: string): string {
  switch (extension.toLowerCase()) {
    case '.pdf':
      return 'PictureAsPdf';
    case '.doc':
    case '.docx':
      return 'Article';
    case '.md':
    case '.txt':
      return 'Description';
    case '.html':
    case '.htm':
    case '.json':
    case '.xml':
      return 'Code';
    case '.csv':
      return 'TableChart';
    default:
      return 'InsertDriveFile';
  }
}

export function isTextDocument(extension: string): boolean {
  return ['.md', '.txt'].includes(extension.toLowerCase());
}

export function sanitizeFileName(title: string): string {
  let sanitized = title.trim().replace(/\s+/g, '-');
  sanitized = sanitized.replace(/[^\w\-.]/g, '');
  sanitized = sanitized.replace(/-{2,}/g, '-');
  return sanitized || 'document';
}

export const ACCEPTED_FILE_TYPES: Record<string, string[]> = {
  'text/plain': ['.txt'],
  'text/markdown': ['.md'],
  'application/pdf': ['.pdf'],
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': ['.docx'],
  'text/html': ['.html', '.htm'],
  'application/json': ['.json'],
  'application/xml': ['.xml'],
  'text/csv': ['.csv'],
};

export const MAX_FILE_SIZE = 50 * 1024 * 1024; // 50 MB
