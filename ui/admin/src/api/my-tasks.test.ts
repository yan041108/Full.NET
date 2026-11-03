import { beforeEach, expect, it, vi } from 'vitest';
import { isCursorPage } from '@fullnet/client-contracts';
import { listMyImportTasks, listMyReportingTasks } from './my-tasks';
const request = vi.hoisted(() => vi.fn());
vi.mock('./http', () => ({ http: { request } }));
beforeEach(() => vi.resetAllMocks());
it('本人查询使用生成操作、固定页上限、来源筛选和取消信号', async () => {
  request.mockResolvedValue({ items: [], nextCursor: null, hasMore: false });
  const signal = new AbortController().signal;
  await listMyImportTasks('before', 'execution_failed', signal);
  const url = new URL(request.mock.calls[0]![0], 'https://localhost');
  expect(url.pathname).toBe('/api/v1/import-export/tasks/mine');
  expect([...url.searchParams]).toEqual([['pageSize', '20'], ['statusKey', 'execution_failed'], ['cursor', 'before']]);
  expect(request.mock.calls[0]![2]).toBe(signal);
  await listMyReportingTasks(undefined, 'failed', signal);
  expect(request.mock.lastCall![0]).toBe('/api/v1/reporting/export-tasks/mine?pageSize=20&statusKey=failed');
});
it('生成契约与游标交叉约束共同拒绝损坏的来源响应', async () => {
  for (const value of [{ items: [], nextCursor: 'again', hasMore: true }, { items: [], nextCursor: 'again', hasMore: false },
    { items: [{ id: 'invalid' }], nextCursor: null, hasMore: false }]) {
    request.mockResolvedValue(value); await expect(listMyReportingTasks()).rejects.toThrow();
  }
});
it('游标页拒绝超过接口上限或缺失后续位置的摘要', () => {
  const item = (value: unknown): value is number => typeof value === 'number';
  expect(isCursorPage({ items: Array(101).fill(1), nextCursor: null, hasMore: false }, item)).toBe(false);
  expect(isCursorPage({ items: [1], nextCursor: null, hasMore: true }, item)).toBe(false);
  expect(isCursorPage({ items: [1], nextCursor: 'next', hasMore: true }, item)).toBe(true);
});
