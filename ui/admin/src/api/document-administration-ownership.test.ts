import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getDocumentPermissionsByDocument, setDocumentPermissions } from './document-permissions';
import { restoreRecycleBinItem } from './document-recycle-bin';
import { shareDocument, shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http',()=>({http:{request:vi.fn()}}));
const permission={id:shareId,documentId:shareDocument.id,userId:shareId,permissionLevel:'read',createdAtUtc:'2026-10-10T00:00:00Z'};
const req={documentId:shareDocument.id,permissions:[{userId:shareId,permissionLevel:'read'}]};
beforeEach(()=>vi.resetAllMocks());
describe('文档权限与恢复响应归属',()=>{
  it('读取拒绝其他文档权限',async()=>{vi.mocked(http.request).mockResolvedValue([{...permission,documentId:otherShareId}]);await expect(getDocumentPermissionsByDocument(req.documentId)).rejects.toThrow(/^client.invalid_document_permission_identity$/);});
  it.each([
    {value:[{...permission,documentId:otherShareId}]},
    {value:[{...permission,userId:otherShareId}]},
    {value:[{...permission,permissionLevel:'write'}]},
    {value:[]},{value:[permission,permission]}
  ])('设置拒绝错配或不完整权限集合 %j',async ({value})=>{vi.mocked(http.request).mockResolvedValue(value);await expect(setDocumentPermissions(req)).rejects.toThrow(/^client.invalid_document_permission_identity$/);});
  it('空授权集合允许读取和显式设置',async()=>{vi.mocked(http.request).mockResolvedValue([]);await expect(getDocumentPermissionsByDocument(req.documentId)).resolves.toEqual([]);await expect(setDocumentPermissions({...req,permissions:[]})).resolves.toEqual([]);});
  it('设置允许乱序和UUID大小写并传递取消信号',async()=>{const signal=new AbortController().signal;const second={...permission,id:otherShareId,userId:otherShareId};vi.mocked(http.request).mockResolvedValue([second,{...permission,documentId:permission.documentId.toUpperCase(),userId:shareId.toUpperCase()}]);await expect(setDocumentPermissions({...req,permissions:[...req.permissions,{userId:otherShareId,permissionLevel:'read'}]},signal)).resolves.toHaveLength(2);expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);});
  it('设置归属使用已发送快照，调用方迟改参数不能迁移响应',async()=>{const pending=deferred<unknown>();vi.mocked(http.request).mockReturnValue(pending.promise);const mutable={documentId:req.documentId,permissions:[{userId:shareId,permissionLevel:'read'}]};const result=setDocumentPermissions(mutable);mutable.documentId=otherShareId;mutable.permissions[0]!.userId=otherShareId;pending.resolve([permission]);await expect(result).resolves.toEqual([permission]);});
  it.each([{requested:' \u0085 read \u0085 ',returned:'read'},{requested:'\uFEFFread',returned:'\uFEFFread'}])('设置与服务端裁剪一致 %j',async({requested,returned})=>{vi.mocked(http.request).mockResolvedValue([{...permission,permissionLevel:returned}]);await expect(setDocumentPermissions({...req,permissions:[{userId:shareId,permissionLevel:requested}]})).resolves.toHaveLength(1);});
  it('恢复拒绝其他条目',async()=>{vi.mocked(http.request).mockResolvedValue({...shareDocument,id:otherShareId});await expect(restoreRecycleBinItem(shareDocument.id,{version:2})).rejects.toThrow(/^client.invalid_recycle_bin_identity$/);});
  it('恢复允许UUID大小写并传递信号',async()=>{const signal=new AbortController().signal;vi.mocked(http.request).mockResolvedValue({...shareDocument,id:shareDocument.id.toUpperCase()});await expect(restoreRecycleBinItem(shareDocument.id,{version:2},signal)).resolves.toMatchObject({id:shareDocument.id.toUpperCase()});expect(vi.mocked(http.request).mock.calls[0]?.[2]).toBe(signal);});
});
