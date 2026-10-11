import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElInput, ElMessageBox } from 'element-plus';
import Permissions from './DocumentPermissionsView.vue';
import Recycle from './DocumentRecycleBinView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as documents from '../api/host-document-items';
import * as acl from '../api/document-permissions';
import * as recycle from '../api/document-recycle-bin';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, shareDocument, shareId, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';

vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
vi.mock('../api/document-permissions', () => ({ getDocumentPermissionsByDocument: vi.fn(), setDocumentPermissions: vi.fn() }));
vi.mock('../api/document-recycle-bin', () => ({ listRecycleBinItems: vi.fn(), restoreRecycleBinItem: vi.fn(), purgeRecycleBinItem: vi.fn() }));
const doc = { ...shareDocument, title: '原文档' };
const second = { ...doc, id: otherShareId, title: '第二文档' };
const permission = { id: shareId, documentId: doc.id, userId: shareId, permissionLevel: 'read', createdAtUtc: '2026-10-10T00:00:00Z' };
const page = { items: [doc, second], page: 1, pageSize: 20, total: 2 };
const read = 'document.host_documents.read', aclRead = 'document.host_permissions.read', set = 'document.host_permissions.set';
const recycleRead = 'document.host_recycle_bin.read', restore = 'document.host_recycle_bin.restore', purge = 'document.host_recycle_bin.purge';
let wrapper: VueWrapper | undefined;
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(documents.listDocumentItems).mockResolvedValue(page);
  vi.mocked(acl.getDocumentPermissionsByDocument).mockResolvedValue([permission]); vi.mocked(acl.setDocumentPermissions).mockResolvedValue([permission]);
  vi.mocked(recycle.listRecycleBinItems).mockResolvedValue(page); vi.mocked(recycle.restoreRecycleBinItem).mockResolvedValue(doc); vi.mocked(recycle.purgeRecycleBinItem).mockResolvedValue(true);
});
afterEach(async () => { wrapper?.unmount(); wrapper = undefined; ElMessageBox.close(); await flushPromises(); document.body.replaceChildren(); vi.restoreAllMocks(); });
async function setup(component: typeof Permissions | typeof Recycle, permissions: string[], tenant = false, realDialog = false) {
  const context = createShareSession(permissions);
  if (tenant) context.session.currentUser!.scope = 'tenant:' + shareId.replaceAll('-', '');
  wrapper = mount(component, { attachTo: realDialog ? document.body : undefined, global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: realDialog ? {} : { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } });
  await flushPromises(); return context.session;
}
const click = (id: string) => wrapper!.get(`[data-testid="${id}"]`).trigger('click');
const dialog = () => wrapper!.getComponent(ArtFormDialog);
const save = () => dialog().vm.$emit('confirm');
async function openAcl(index = 0) { await wrapper!.findAll('[data-testid="document-permissions-set"]')[index]!.trigger('click'); await flushPromises(); }
async function inputAcl(user = otherShareId, level = 'write') { const inputs = dialog().findAllComponents(ElInput); await inputs[0]!.setValue(user); await inputs[1]!.setValue(level); }

describe('文档权限编辑归属与整份快照', () => {
  const all = [read, aclRead, set];
  it.each([{permissions:[]}, {permissions:[aclRead,set]}, {permissions:[read,set]}])('缺少父read不读取或显示 %j', async ({permissions}) => { await setup(Permissions, permissions); expect(documents.listDocumentItems).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain('原文档'); });
  it('租户上下文不读取Host文档', async () => { await setup(Permissions, all, true); expect(documents.listDocumentItems).not.toHaveBeenCalled(); });
  it('撤权同步取消列表并清理旧页面', async () => { const session = await setup(Permissions, all); const pending=deferred<typeof page>(); vi.mocked(documents.listDocumentItems).mockReturnValue(pending.promise); await wrapper!.get('button').trigger('click'); const signal=vi.mocked(documents.listDocumentItems).mock.calls.at(-1)?.[3]; session.currentUser!.permissions=[]; expect(signal?.aborted).toBe(true); pending.resolve(page); await flushPromises(); expect(wrapper!.text()).not.toContain('原文档'); });
  it('切换文档取消目录，旧结果不能接入新对象', async () => { await setup(Permissions, all); const old=deferred<typeof permission[]>(); vi.mocked(acl.getDocumentPermissionsByDocument).mockReturnValueOnce(old.promise).mockResolvedValueOnce([{...permission,documentId:second.id,userId:otherShareId}]); await openAcl(); const signal=vi.mocked(acl.getDocumentPermissionsByDocument).mock.calls[0]?.[1]; await openAcl(1); expect(signal?.aborted).toBe(true); old.resolve([permission]); await flushPromises(); expect(dialog().text()).toContain(otherShareId); expect(dialog().text()).not.toContain(shareId); });
  it.each(['resolve','reject'] as const)('关闭目录取消，迟到%s不回填', async outcome => { await setup(Permissions, all); const pending=deferred<typeof permission[]>(); vi.mocked(acl.getDocumentPermissionsByDocument).mockReturnValue(pending.promise); await openAcl(); const signal=vi.mocked(acl.getDocumentPermissionsByDocument).mock.calls[0]?.[1]; dialog().vm.$emit('update:open',false); expect(signal?.aborted).toBe(true); if(outcome==='resolve')pending.resolve([permission]);else pending.reject(new Error('late'));await flushPromises();expect(dialog().props('open')).toBe(false);expect(wrapper!.find('.el-alert--error').exists()).toBe(false); });
  it('成功读取前及读取失败后不能整体保存', async()=>{await setup(Permissions,all);const pending=deferred<typeof permission[]>();vi.mocked(acl.getDocumentPermissionsByDocument).mockReturnValue(pending.promise);await openAcl();await inputAcl();save();expect(acl.setDocumentPermissions).not.toHaveBeenCalled();pending.reject(new Error('load failed'));await flushPromises();save();expect(acl.setDocumentPermissions).not.toHaveBeenCalled();});
  it('追加用户保留其他授权',async()=>{await setup(Permissions,all);await openAcl();await inputAcl();save();await flushPromises();expect(vi.mocked(acl.setDocumentPermissions).mock.calls[0]?.[0]).toEqual({documentId:doc.id,permissions:[{userId:shareId,permissionLevel:'read'},{userId:otherShareId,permissionLevel:'write'}]});});
  it('同用户大小写等价更新，保留其他用户且不重复',async()=>{await setup(Permissions,all);vi.mocked(acl.getDocumentPermissionsByDocument).mockResolvedValue([permission,{...permission,id:otherShareId,userId:otherShareId}]);await openAcl();await inputAcl(shareId.toUpperCase(),'write');save();await flushPromises();const entries=vi.mocked(acl.setDocumentPermissions).mock.calls[0]?.[0].permissions;expect(entries).toEqual([{userId:otherShareId,permissionLevel:'read'},{userId:shareId.toUpperCase(),permissionLevel:'write'}]);});
  it.each(['resolve','reject'] as const)('关闭保存取消，迟到%s不反馈且重开清空输入',async outcome=>{await setup(Permissions,all);const pending=deferred<typeof permission[]>();vi.mocked(acl.setDocumentPermissions).mockReturnValue(pending.promise);const success=vi.spyOn(feedback,'showSuccess');const error=vi.spyOn(feedback,'showProblem');await openAcl();await inputAcl();save();save();expect(acl.setDocumentPermissions).toHaveBeenCalledOnce();const signal=vi.mocked(acl.setDocumentPermissions).mock.calls[0]?.[1];dialog().vm.$emit('update:open',false);expect(signal?.aborted).toBe(true);if(outcome==='resolve')pending.resolve([permission]);else pending.reject(new Error('late'));await flushPromises();expect(success).not.toHaveBeenCalled();expect(error).not.toHaveBeenCalled();await openAcl();expect(dialog().findAllComponents(ElInput)[0]!.props('modelValue')).toBe('');});
  it('关闭后和撤销set后直接确认不写入',async()=>{const session=await setup(Permissions,all);await openAcl();await inputAcl();dialog().vm.$emit('update:open',false);save();await openAcl();await inputAcl();session.currentUser!.permissions=[read,aclRead];save();await flushPromises();expect(acl.setDocumentPermissions).not.toHaveBeenCalled();});
  it('空权限级别不能提交',async()=>{await setup(Permissions,all);await openAcl();await inputAcl(otherShareId,'  ');save();await flushPromises();expect(acl.setDocumentPermissions).not.toHaveBeenCalled();});
  it('保存响应失败使旧快照失效，重读后保留已提交授权',async()=>{
    await setup(Permissions,all);await openAcl();await inputAcl();
    vi.mocked(acl.setDocumentPermissions).mockRejectedValueOnce(new Error('committed but response lost'));
    save();await flushPromises();const third='01912345-6789-7abc-8def-0123456789af';
    await inputAcl(third);save();await flushPromises();expect(acl.setDocumentPermissions).toHaveBeenCalledOnce();
    vi.mocked(acl.getDocumentPermissionsByDocument).mockResolvedValue([permission,{...permission,id:otherShareId,userId:otherShareId,permissionLevel:'write'}]);
    await openAcl();await inputAcl(third);save();await flushPromises();
    expect(vi.mocked(acl.setDocumentPermissions).mock.calls[1]?.[0].permissions).toEqual([{userId:shareId,permissionLevel:'read'},{userId:otherShareId,permissionLevel:'write'},{userId:third,permissionLevel:'write'}]);
  });
});

describe('回收站批量操作归属',()=>{
  const all=[recycleRead,restore,purge];
  const select=(rows:typeof doc[])=>wrapper!.getComponent({name:'ElTable'}).vm.$emit('selection-change',rows);
  const confirm=()=>wrapper!.getComponent(ArtFormDialog).vm.$emit('confirm');
  it.each([{permissions:[]},{permissions:[restore,purge]}])('缺少父read不读取或显示 %j',async ({permissions})=>{await setup(Recycle,permissions);expect(recycle.listRecycleBinItems).not.toHaveBeenCalled();expect(wrapper!.text()).not.toContain('原文档');});
  it('租户上下文不读取Host回收站',async()=>{await setup(Recycle,all,true);expect(recycle.listRecycleBinItems).not.toHaveBeenCalled();});
  it.each(['resolve','reject'] as const)('撤权取消恢复，迟到%s不反馈或刷新',async outcome=>{const session=await setup(Recycle,all);const pending=deferred<typeof doc>();vi.mocked(recycle.restoreRecycleBinItem).mockReturnValue(pending.promise);const success=vi.spyOn(feedback,'showSuccess');await click('document-recycle-restore');const signal=vi.mocked(recycle.restoreRecycleBinItem).mock.calls[0]?.[2];session.currentUser!.permissions=[];expect(signal?.aborted).toBe(true);if(outcome==='resolve')pending.resolve(doc);else pending.reject(new Error('late'));await flushPromises();expect(success).not.toHaveBeenCalled();expect(recycle.listRecycleBinItems).toHaveBeenCalledOnce();});
  it('批量恢复冻结目标并阻止重复，选择变化不改变后续写入',async()=>{await setup(Recycle,all);const pending=deferred<typeof doc>();vi.mocked(recycle.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);select([doc,second]);await flushPromises();await click('document-recycle-batch-restore');await click('document-recycle-batch-restore');expect(recycle.restoreRecycleBinItem).toHaveBeenCalledOnce();select([{...second,id:shareId}]);pending.resolve(doc);await flushPromises();expect(vi.mocked(recycle.restoreRecycleBinItem).mock.calls.map(call=>call[0])).toEqual([doc.id,second.id]);});
  it('批量第一项完成前撤权，不再发送剩余项',async()=>{const session=await setup(Recycle,all);const pending=deferred<typeof doc>();vi.mocked(recycle.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);select([doc,second]);await flushPromises();await click('document-recycle-batch-restore');session.currentUser!.permissions=[recycleRead];pending.resolve(doc);await flushPromises();expect(recycle.restoreRecycleBinItem).toHaveBeenCalledOnce();});
  it('批量恢复冻结每项版本，等待期间原对象变更不改请求',async()=>{await setup(Recycle,all);const pending=deferred<typeof doc>();const mutable={...second};vi.mocked(recycle.restoreRecycleBinItem).mockReturnValueOnce(pending.promise);select([doc,mutable]);await flushPromises();await click('document-recycle-batch-restore');mutable.version=99;pending.resolve(doc);await flushPromises();expect(vi.mocked(recycle.restoreRecycleBinItem).mock.calls[1]?.[1]).toEqual({version:second.version});});
  it.each(['revoke','unmount'] as const)('真实清除确认在%s后回收且不删除',async outcome=>{const session=await setup(Recycle,all,false,true);await click('document-recycle-purge');await flushPromises();const dialogs=()=>[...document.querySelectorAll('[role="dialog"]')].filter(element=>element.textContent?.includes('原文档'));expect(dialogs()).toHaveLength(1);if(outcome==='revoke')session.currentUser!.permissions=[recycleRead];else{wrapper!.unmount();wrapper=undefined;}await flushPromises();expect(dialogs()).toHaveLength(0);expect(recycle.purgeRecycleBinItem).not.toHaveBeenCalled();});
  it('批量清除确认冻结选择，等待时防重复',async()=>{await setup(Recycle,all);select([doc,second]);await flushPromises();await click('document-recycle-batch-purge');await click('document-recycle-batch-purge');expect(wrapper!.findAllComponents(ArtFormDialog)).toHaveLength(1);select([doc]);confirm();await flushPromises();expect(vi.mocked(recycle.purgeRecycleBinItem).mock.calls.map(call=>call[0])).toEqual([doc.id,second.id]);});
  it('清除false停止批次，不报成功或刷新',async()=>{await setup(Recycle,all);vi.mocked(recycle.purgeRecycleBinItem).mockResolvedValue(false);const success=vi.spyOn(feedback,'showSuccess');const error=vi.spyOn(feedback,'showProblem');select([doc,second]);await flushPromises();await click('document-recycle-batch-purge');confirm();await flushPromises();expect(recycle.purgeRecycleBinItem).toHaveBeenCalledOnce();expect(success).not.toHaveBeenCalled();expect(error).toHaveBeenCalledOnce();expect(recycle.listRecycleBinItems).toHaveBeenCalledOnce();});
});
