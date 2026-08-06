import {
  isHostMenu,
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
