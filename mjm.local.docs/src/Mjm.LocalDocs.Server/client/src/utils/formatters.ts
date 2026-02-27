export function formatFileSize(bytes: number): string {
  if (bytes === 0) return '0 B';
  const sizes = ['B', 'KB', 'MB', 'GB'];
  let order = 0;
  let size = bytes;
  while (size >= 1024 && order < sizes.length - 1) {
    order++;
    size /= 1024;
  }
  return `${size.toFixed(2).replace(/\.?0+$/, '')} ${sizes[order]}`;
}

export function formatDate(iso: string): string {
  return new Date(iso).toLocaleString('it-IT', {
    day: '2-digit',
    month: '2-digit',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  });
}
