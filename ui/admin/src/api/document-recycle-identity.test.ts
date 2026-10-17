import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listRecycleBinItems, restoreRecycleBinItem, purgeRecycleBinItem } from './document-recycle-bin';
import { recycleItem as row, recyclePage, restoredRecycleItem } from '../test/document-recycle-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn(), requestBlob: vi.fn() } }));
const request = vi.mocked(http.request), other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
beforeEach(() => vi.resetAllMocks());
describe('回收站请求与结果归属', () => {
  it.each([{ page: 2 }, { pageSize: 10 }, { total: -1 }, { total: Number.MAX_SAFE_INTEGER + 1 }, { items: [row, { ...row, id: row.id.toUpperCase() }] }, { items: [{ ...row, version: 0 }] }, { items: [{ ...row, deletedAtUtc: null }] }, { items: [{ ...row, deletedByUserId: null }] }])('拒绝错误分页、重复身份和非删除行 %j', async change => {
    request.mockResolvedValueOnce({ ...recyclePage(), ...change }); await expect(listRecycleBinItems()).rejects.toThrow();
  });
  it('按服务端边界归一分页，业务状态不代表是否软删除', async () => {
    request.mockResolvedValueOnce(recyclePage([row], 1, 100)); await expect(listRecycleBinItems(0, 200)).resolves.toMatchObject({ pageSize: 100 });
    expect(request).toHaveBeenCalledWith('/api/v1/document/host/recycle-bin?page=1&pageSize=100', { method: 'GET' }, undefined);
  });
  it.each([0, 0.5, Number.NaN, Number.MAX_SAFE_INTEGER + 1])('非法恢复修订 %s 在请求前拒绝', async version => {
    await expect(restoreRecycleBinItem(row.id, { version })).rejects.toThrow(); expect(request).not.toHaveBeenCalled();
  });
  it.each([{ id: other }, { version: row.version }, { deletedAtUtc: row.deletedAtUtc }, { deletedByUserId: row.deletedByUserId }])('恢复拒绝错误身份、旧修订与仍删除的响应 %j', async change => {
    request.mockResolvedValueOnce({ ...restoredRecycleItem(), ...change }); await expect(restoreRecycleBinItem(row.id, { version: row.version })).rejects.toThrow();
  });
  it('恢复冻结发送修订，调用方后改不影响响应校验', async () => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const body = { version: row.version }, action = restoreRecycleBinItem(row.id, body); body.version = 99;
    pending.resolve(restoredRecycleItem()); await expect(action).resolves.toMatchObject({ version: row.version + 1 });
    expect(JSON.parse(request.mock.calls[0]![1]!.body as string)).toEqual({ version: row.version });
  });
  it.each(['', '00000000-0000-0000-0000-000000000000'])('空资源标识 %j 不发送永久清除', async id => {
    await expect(purgeRecycleBinItem(id)).rejects.toThrow(); expect(request).not.toHaveBeenCalled();
  });
  it('永久清除 false 是明确未完成，不改报成功', async () => {
    request.mockResolvedValueOnce(false); await expect(purgeRecycleBinItem(row.id)).resolves.toBe(false);
  });
});
