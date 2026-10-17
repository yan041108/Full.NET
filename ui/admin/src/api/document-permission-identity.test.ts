import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDocumentPermissionSnapshot, getDocumentPermissionsByDocument, setDocumentPermissions } from './document-permissions';
import { permissionEntry as entry, otherPermissionEntry as other, permissionSnapshot as snapshot } from '../test/document-permission-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const request = vi.mocked(http.request), nil = '00000000-0000-0000-0000-000000000000';
const body = () => ({ documentId: entry.documentId, version: 7, permissions: [{ userId: entry.userId, permissionLevel: entry.permissionLevel }] });
beforeEach(() => vi.resetAllMocks());
describe('文档权限快照与替换身份', () => {
  it.each([{ permissions: [entry, { ...other, id: entry.id.toUpperCase() }] }, { permissions: [{ ...entry, userId: nil }] }, { permissions: [{ ...entry, id: nil }] }, { permissions: [{ ...entry, permissionLevel: ' \u0085 ' }] }, { documentId: nil }])('快照拒绝空身份、重复行与空白配置 %j', async change => {
    request.mockResolvedValueOnce({ ...snapshot(), ...change }); await expect(getDocumentPermissionSnapshot(entry.documentId)).rejects.toThrow();
  });
  it.each(['', nil])('非法文档标识 %j 在读取前拒绝', async id => {
    await expect(getDocumentPermissionSnapshot(id)).rejects.toThrow(); await expect(getDocumentPermissionsByDocument(id)).rejects.toThrow(); expect(request).not.toHaveBeenCalled();
  });
  it.each([{ documentId: nil }, { version: 0 }, { permissions: [{ userId: nil, permissionLevel: 'read' }] }, { permissions: [{ userId: entry.userId, permissionLevel: ' \u0085 ' }] }])('替换请求 %j 在发送前拒绝', async change => {
    await expect(setDocumentPermissions({ ...body(), ...change })).rejects.toThrow(); expect(request).not.toHaveBeenCalled();
  });
  it('查询拒绝重复权限标识而不把重复内容当作完整集合', async () => {
    request.mockResolvedValueOnce([entry, { ...other, id: entry.id }]); await expect(getDocumentPermissionsByDocument(entry.documentId)).rejects.toThrow();
  });
  it('响应集合身份不能因匹配用户和级别而掩盖重复行标识', async () => {
    request.mockResolvedValueOnce([entry, { ...other, id: entry.id }]); await expect(setDocumentPermissions({ ...body(), permissions: [entry, other] })).rejects.toThrow();
  });
  it('保留真实服务端允许的同用户多条配置，不暗加唯一用户规则', async () => {
    const second = { ...other, userId: entry.userId }; request.mockResolvedValueOnce(snapshot([entry, second])); await expect(getDocumentPermissionSnapshot(entry.documentId)).resolves.toMatchObject({ permissions: [entry, second] });
  });
  it('整份替换保留未修改用户的多条配置并按多重集合核对', async () => {
    const second = { ...other, userId: entry.userId }; request.mockResolvedValueOnce([second, entry]); await expect(setDocumentPermissions({ ...body(), permissions: [entry, second] })).resolves.toEqual([second, entry]);
  });
  it('冻结嵌套配置，调用方迟改不能迁移本次整份替换', async () => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const payload = body(), action = setDocumentPermissions(payload); payload.permissions[0]!.permissionLevel = 'changed'; payload.permissions.push({ userId: other.userId, permissionLevel: 'write' });
    pending.resolve([entry]); await expect(action).resolves.toEqual([entry]); expect(JSON.parse(request.mock.calls[0]![1]!.body as string).permissions).toEqual([{ userId: entry.userId, permissionLevel: 'read' }]);
  });
});
