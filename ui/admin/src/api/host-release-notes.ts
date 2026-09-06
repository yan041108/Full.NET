import {
  isHostReleaseNote,
  isHostReleaseNotePage,
  type CreateHostReleaseNoteRequest,
  type HostReleaseNote,
  type HostReleaseNoteListQuery,
  type HostReleaseNotePage,
  type UpdateHostReleaseNoteRequest
} from '@fullnet/client-contracts';
import { request } from './http';

function buildListQuery(query: HostReleaseNoteListQuery): string {
  const params = new URLSearchParams();
  params.set('page', String(query.page ?? 1));
  params.set('pageSize', String(query.pageSize ?? 20));
  if (query.title?.trim()) {
    params.set('title', query.title.trim());
  }
  if (query.status) {
    params.set('status', query.status);
  }
  if (query.versionLabel?.trim()) {
    params.set('versionLabel', query.versionLabel.trim());
  }
  return params.toString();
}

/** 分页查询 Host 更新日志列表，并对响应页做失败关闭校验。 */
export async function listHostReleaseNotes(
  query: HostReleaseNoteListQuery = {},
  signal?: AbortSignal
): Promise<HostReleaseNotePage> {
  const value = await request<unknown>(
    `/api/v1/platform/host-release-notes?${buildListQuery(query)}`,
    { method: 'GET' },
    signal
  );
  if (!isHostReleaseNotePage(value)) {
    throw new Error('client.invalid_host_release_note_page');
  }

  return value;
}

/** 创建 Host 更新日志草稿。 */
export async function createHostReleaseNote(
  body: CreateHostReleaseNoteRequest,
  signal?: AbortSignal
): Promise<HostReleaseNote> {
  const value = await request<unknown>(
    '/api/v1/platform/host-release-notes',
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isHostReleaseNote(value)) {
    throw new Error('client.invalid_host_release_note');
  }

  return value;
}

/** 更新 Host 更新日志草稿，并携带版本号维持乐观并发。 */
export async function updateHostReleaseNote(
  id: string,
  body: UpdateHostReleaseNoteRequest,
  signal?: AbortSignal
): Promise<HostReleaseNote> {
  const value = await request<unknown>(
    `/api/v1/platform/host-release-notes/${id}`,
    {
      method: 'PUT',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body)
    },
    signal
  );
  if (!isHostReleaseNote(value)) {
    throw new Error('client.invalid_host_release_note');
  }

  return value;
}

/** 发布 Host 更新日志。 */
export async function publishHostReleaseNote(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostReleaseNote> {
  const value = await request<unknown>(
    `/api/v1/platform/host-release-notes/${id}/publish`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    },
    signal
  );
  if (!isHostReleaseNote(value)) {
    throw new Error('client.invalid_host_release_note');
  }

  return value;
}

/** 撤回已发布的 Host 更新日志。 */
export async function retractHostReleaseNote(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<HostReleaseNote> {
  const value = await request<unknown>(
    `/api/v1/platform/host-release-notes/${id}/retract`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    },
    signal
  );
  if (!isHostReleaseNote(value)) {
    throw new Error('client.invalid_host_release_note');
  }

  return value;
}

/** 删除 Host 更新日志草稿。 */
export async function deleteHostReleaseNote(
  id: string,
  version: number,
  signal?: AbortSignal
): Promise<void> {
  await request<unknown>(
    `/api/v1/platform/host-release-notes/${id}/delete`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify({ version })
    },
    signal
  );
}

export type { HostReleaseNote, HostReleaseNotePage };
