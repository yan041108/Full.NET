import {
  isHostFile,
  isHostFilePage,
  type HostFile,
  type HostFilePage
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostFiles(
  page = 1,
  pageSize = 20
): Promise<HostFilePage> {
  const value = await request<unknown>(
    `/api/v1/files/host-files?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostFilePage(value)) {
    throw new Error('client.invalid_host_file_page');
  }
  return value;
}

export async function uploadHostFile(file: File): Promise<HostFile> {
  const formData = new FormData();
  formData.append('file', file);
  const value = await request<unknown>('/api/v1/files/host-files', {
    method: 'POST',
    body: formData
  });
  if (!isHostFile(value)) {
    throw new Error('client.invalid_host_file');
  }
  return value;
}

export async function deleteHostFile(id: string): Promise<HostFile> {
  const value = await request<unknown>(
    `/api/v1/files/host-files/${encodeURIComponent(id)}/delete`,
    { method: 'POST' }
  );
  if (!isHostFile(value)) {
    throw new Error('client.invalid_host_file');
  }
  return value;
}

export function hostFileContentUrl(id: string): string {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ?? '';
  return `${apiBaseUrl}/api/v1/files/host-files/${encodeURIComponent(id)}/content`;
}
