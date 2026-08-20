import {
  isHostMenu,
  isHostMenuArray,
  isHostMenuPermissionOptionArray,
  isHostMenuPage,
  type CreateHostMenuRequest,
  type HostMenu,
  type HostMenuPermissionOption,
  type HostMenuPage,
  type UpdateHostMenuRequest
} from '@fullnet/client-contracts';
import { request } from './http';

export async function listHostMenus(
  page = 1,
  pageSize = 20
): Promise<HostMenuPage> {
  const value = await request<unknown>(
    `/api/v1/identity/menus?page=${page}&pageSize=${pageSize}`
  );
  if (!isHostMenuPage(value)) throw new Error('client.invalid_host_menu_page');
  return value;
}

export async function listHostMenusAll(): Promise<HostMenu[]> {
  const value = await request<unknown>('/api/v1/identity/menus/all');
  if (!isHostMenuArray(value)) throw new Error('client.invalid_host_menu_array');
  return value;
}

export async function createHostMenu(
  body: CreateHostMenuRequest
): Promise<HostMenu> {
  const value = await request<unknown>('/api/v1/identity/menus', {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!isHostMenu(value)) throw new Error('client.invalid_host_menu');
  return value;
}

export async function listHostMenuPermissionOptions(): Promise<HostMenuPermissionOption[]> {
  const value = await request<unknown>('/api/v1/identity/menus/permission-options');
  if (!isHostMenuPermissionOptionArray(value)) {
    throw new Error('client.invalid_host_menu_permission_options');
  }

  return value;
}

export async function updateHostMenu(
  id: string,
  body: UpdateHostMenuRequest
): Promise<HostMenu> {
  const value = await request<unknown>(
    `/api/v1/identity/menus/${encodeURIComponent(id)}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    }
  );
  if (!isHostMenu(value)) throw new Error('client.invalid_host_menu');
  return value;
}

export async function disableHostMenu(id: string): Promise<HostMenu> {
  const value = await request<unknown>(
    `/api/v1/identity/menus/${encodeURIComponent(id)}/disable`,
    { method: 'POST' }
  );
  if (!isHostMenu(value)) throw new Error('client.invalid_host_menu');
  return value;
}

export async function enableHostMenu(id: string): Promise<HostMenu> {
  const value = await request<unknown>(
    `/api/v1/identity/menus/${encodeURIComponent(id)}/enable`,
    { method: 'POST' }
  );
  if (!isHostMenu(value)) throw new Error('client.invalid_host_menu');
  return value;
}

export async function syncHostMenuCatalog(): Promise<{
  created: number;
  skipped: number;
  reparented: number;
}> {
  const value = await request<unknown>(
    '/api/v1/identity/menus/sync-catalog',
    { method: 'POST' }
  );
  if (
    typeof value !== 'object'
    || value === null
    || !('created' in value)
    || !('skipped' in value)
    || !('reparented' in value)
    || typeof value.created !== 'number'
    || typeof value.skipped !== 'number'
    || typeof value.reparented !== 'number'
  ) {
    throw new Error('client.invalid_host_menu_sync_result');
  }
  return {
    created: value.created as number,
    skipped: value.skipped as number,
    reparented: value.reparented as number
  };
}
