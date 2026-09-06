import {
  isMyReleaseNote,
  isMyReleaseNotePage,
  type MyReleaseNote,
  type MyReleaseNotePage
} from '@fullnet/client-contracts';
import { request } from './http';

/** 分页查询当前用户可见的已发布更新日志。 */
export async function listMyReleaseNotes(
  page = 1,
  pageSize = 20,
  signal?: AbortSignal
): Promise<MyReleaseNotePage> {
  const params = new URLSearchParams({
    page: String(page),
    pageSize: String(pageSize)
  });
  const value = await request<unknown>(
    `/api/v1/platform/my-release-notes?${params.toString()}`,
    { method: 'GET' },
    signal
  );
  if (!isMyReleaseNotePage(value)) {
    throw new Error('client.invalid_my_release_note_page');
  }

  return value;
}

/** 获取当前用户最新未读更新日志；无未读时返回 null。 */
export async function getLatestUnreadReleaseNote(
  signal?: AbortSignal
): Promise<MyReleaseNote | null> {
  const value = await request<unknown>(
    '/api/v1/platform/my-release-notes/latest-unread',
    { method: 'GET' },
    signal
  );
  if (value === undefined) {
    return null;
  }
  if (!isMyReleaseNote(value)) {
    throw new Error('client.invalid_my_release_note');
  }

  return value;
}

/** 将更新日志标记为已读；重复调用幂等。 */
export async function markMyReleaseNoteRead(
  id: string,
  signal?: AbortSignal
): Promise<MyReleaseNote> {
  const value = await request<unknown>(
    `/api/v1/platform/my-release-notes/${id}/read`,
    {
      method: 'POST',
      headers: { 'content-type': 'application/json' }
    },
    signal
  );
  if (!isMyReleaseNote(value)) {
    throw new Error('client.invalid_my_release_note');
  }

  return value;
}

export type { MyReleaseNote, MyReleaseNotePage };
