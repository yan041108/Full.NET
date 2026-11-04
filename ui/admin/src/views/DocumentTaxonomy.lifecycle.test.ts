import { beforeEach, afterEach, describe, it, expect, vi } from 'vitest';
import { mount, flushPromises, type VueWrapper } from '@vue/test-utils';
import { ElInput, ElMessageBox } from 'element-plus';
import Categories from './DocumentCategoriesView.vue';
import Tags from './DocumentTagsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as categoryApi from '../api/host-document-categories';
import * as tagApi from '../api/host-document-tags';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, shareId as id, otherShareId as other } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/host-document-categories', () => ({ listDocumentCategories: vi.fn(), createDocumentCategory: vi.fn(), updateDocumentCategory: vi.fn(), deleteDocumentCategory: vi.fn() }));
vi.mock('../api/host-document-tags', () => ({ listDocumentTags: vi.fn(), createDocumentTag: vi.fn(), updateDocumentTag: vi.fn(), deleteDocumentTag: vi.fn() }));
const common = { id, name: '旧目录', code: 'KEEP', icon: 'book', color: '#123456', description: '保留描述', createdAtUtc: '2026-10-10T00:00:00Z', updatedAtUtc: null, version: 1 };
const category = { ...common, parentId: null, sortOrder: 0 };
const tag = { ...common, useCount: 0, isHot: true, isRecommended: false };
const configs = [
  { kind: 'category', permission: 'document.categories', component: Categories, entity: category, list: categoryApi.listDocumentCategories, create: categoryApi.createDocumentCategory, update: categoryApi.updateDocumentCategory, remove: categoryApi.deleteDocumentCategory, listSignal: 0, createSignal: 7, updateSignal: 9 },
  { kind: 'tag', permission: 'document.tags', component: Tags, entity: tag, list: tagApi.listDocumentTags, create: tagApi.createDocumentTag, update: tagApi.updateDocumentTag, remove: tagApi.deleteDocumentTag, listSignal: 1, createSignal: 7, updateSignal: 9 }
];
let wrapper: VueWrapper | undefined;
beforeEach(() => vi.resetAllMocks());
afterEach(async () => {
  wrapper?.unmount(); wrapper = undefined; ElMessageBox.close(); await flushPromises();
  // 每项只使用本文件的挂载点和弹窗，清理 jsdom 中的过渡残留以隔离真实 DOM 用例。
  document.body.replaceChildren(); vi.restoreAllMocks();
});
for (const config of configs) {
  describe(config.kind + '管理请求归属', () => {
    const read = config.permission + '.read';
    const all = [read, config.permission + '.create', config.permission + '.update', config.permission + '.delete'];
    async function setup(permissions = all, tenant = false, realDialog = false) {
      const context = createShareSession(permissions);
      if (tenant) context.session.currentUser!.scope = 'tenant:' + id.replaceAll('-', '');
      vi.mocked(config.list).mockResolvedValue([config.entity] as never);
      vi.mocked(config.create).mockResolvedValue(config.entity as never);
      vi.mocked(config.update).mockResolvedValue(config.entity as never);
      vi.mocked(config.remove).mockResolvedValue(true);
      wrapper = mount(config.component, { attachTo: realDialog ? document.body : undefined, global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: realDialog ? {} : { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } });
      await flushPromises(); return context.session;
    }
    const button = (action: string) => wrapper!.get(`[data-testid="document-${config.kind}-${action}"]`);
    const dialog = () => wrapper!.findAllComponents(ArtFormDialog).find(component => component.props('confirmTestId') === 'document-' + config.kind + '-editor-submit')!;
    const deleteDialogs = () => wrapper!.findAllComponents(ArtFormDialog).filter(component => component.props('confirmTestId') === 'document-' + config.kind + '-delete-confirm');
    const confirmDelete = () => deleteDialogs()[0]!.vm.$emit('confirm');
    const submit = () => dialog().vm.$emit('confirm');
    it.each(['revoke', 'unmount'] as const)('真实确认DOM在%s后清除目录名', async outcome => {
      const session = await setup(all, false, true);
      await button('delete').trigger('click'); await flushPromises();
      const pendingDialogs = () => [...document.querySelectorAll('[role="dialog"]')].filter(element => element.textContent?.includes('旧目录'));
      expect(pendingDialogs()).toHaveLength(1);
      if (outcome === 'revoke') session.currentUser!.permissions = [read];
      else { wrapper!.unmount(); wrapper = undefined; }
      await flushPromises();
      expect(pendingDialogs()).toHaveLength(0); expect(config.remove).not.toHaveBeenCalled();
    });
    async function openCreate() { await button('create').trigger('click'); await dialog().getComponent(ElInput).setValue('新目录'); }
    it.each([{permissions: []}, {permissions: [config.permission + '.create']}])('无父read不请求或渲染 %j', async ({permissions}) => { await setup(permissions); expect(config.list).not.toHaveBeenCalled(); expect(wrapper!.text()).not.toContain('旧目录'); });
    it('有效租户上下文不进入Host目录', async () => { await setup(all, true); expect(config.list).not.toHaveBeenCalled(); });
    it('会话替换同步取消旧列表并只接入新列表', async () => {
      const ownSession = await setup(); const old = deferred<typeof config.entity[]>(); vi.mocked(config.list).mockReturnValueOnce(old.promise as never).mockResolvedValueOnce([{...config.entity, name:'新目录'}] as never);
      await wrapper!.getComponent({name:'ArtTableHeader'}).vm.$emit('refresh'); await flushPromises();
      const signal = vi.mocked(config.list).mock.calls.at(-1)?.[config.listSignal] as AbortSignal;
      ownSession.currentUser!.sessionId = 'replacement'; expect(signal?.aborted).toBe(true); await flushPromises(); old.resolve([config.entity]); await flushPromises(); expect(wrapper!.text()).toContain('新目录'); expect(wrapper!.text()).not.toContain('旧目录');
    });
    it('刷新替换取消旧请求，旧失败不覆盖新结果', async () => { await setup(); const old = deferred<typeof config.entity[]>(); vi.mocked(config.list).mockReturnValueOnce(old.promise as never).mockResolvedValueOnce([{...config.entity,name:'新目录'}] as never); const header=wrapper!.getComponent({name:'ArtTableHeader'}); header.vm.$emit('refresh'); await flushPromises(); const signal=vi.mocked(config.list).mock.calls.at(-1)?.[config.listSignal] as AbortSignal; header.vm.$emit('refresh'); await flushPromises(); expect(signal?.aborted).toBe(true); old.reject(new Error('旧失败')); await flushPromises(); expect(wrapper!.text()).toContain('新目录'); expect(wrapper!.find('[role="alert"]').exists()).toBe(false); });
    it.each(['resolve','reject'] as const)('关闭创建取消，迟到%s无反馈或刷新', async outcome => { await setup(); const pending=deferred<typeof config.entity>(); vi.mocked(config.create).mockReturnValue(pending.promise as never); const success=vi.spyOn(feedback,'showSuccess'); const error=vi.spyOn(feedback,'showProblem'); await openCreate(); submit(); submit(); expect(config.create).toHaveBeenCalledOnce(); const signal=vi.mocked(config.create).mock.calls[0]?.[config.createSignal] as AbortSignal; dialog().vm.$emit('update:open',false); expect(signal?.aborted).toBe(true); if(outcome==='resolve')pending.resolve(config.entity); else pending.reject(new Error('late')); await flushPromises(); expect(success).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled(); expect(config.list).toHaveBeenCalledOnce(); await button('create').trigger('click'); expect(dialog().getComponent(ElInput).props('modelValue')).toBe(''); });
    it('关闭后直接确认不提交', async()=>{await setup();await openCreate();dialog().vm.$emit('update:open',false);submit();await flushPromises();expect(config.create).not.toHaveBeenCalled();});
    it('编辑撤权取消写入，旧结果不关闭或刷新新上下文',async()=>{const session=await setup();const pending=deferred<typeof config.entity>();vi.mocked(config.update).mockReturnValue(pending.promise as never);const success=vi.spyOn(feedback,'showSuccess');await button('edit').trigger('click');submit();const signal=vi.mocked(config.update).mock.calls[0]?.[config.updateSignal] as AbortSignal;session.currentUser!.permissions=[read];expect(signal?.aborted).toBe(true);pending.resolve(config.entity);await flushPromises();expect(success).not.toHaveBeenCalled();expect(dialog().props('open')).toBe(false);});
    it('删除确认期间撤权，迟到确认不调用API',async()=>{const session=await setup();await button('delete').trigger('click');const stale=deleteDialogs()[0]!;session.currentUser!.permissions=[read];await flushPromises();expect(deleteDialogs()).toHaveLength(0);stale.vm.$emit('confirm');await flushPromises();expect(config.remove).not.toHaveBeenCalled();});
    it('等待确认时防重复删除对话框',async()=>{await setup();await button('delete').trigger('click');await button('delete').trigger('click');expect(deleteDialogs()).toHaveLength(1);confirmDelete();await flushPromises();expect(config.remove).toHaveBeenCalledOnce();});
    it('在途删除撤权取消，迟到成功不反馈或刷新',async()=>{const session=await setup();const pending=deferred<boolean>();vi.mocked(config.remove).mockReturnValue(pending.promise);const success=vi.spyOn(feedback,'showSuccess');await button('delete').trigger('click');confirmDelete();await flushPromises();const signal=vi.mocked(config.remove).mock.calls[0]?.[2];session.currentUser!.permissions=[read];expect(signal?.aborted).toBe(true);await flushPromises();const count=vi.mocked(config.list).mock.calls.length;pending.resolve(true);await flushPromises();expect(success).not.toHaveBeenCalled();expect(config.list).toHaveBeenCalledTimes(count);});
    it('删除返回false不误报成功或刷新',async()=>{await setup();vi.mocked(config.remove).mockResolvedValue(false);const success=vi.spyOn(feedback,'showSuccess');const error=vi.spyOn(feedback,'showProblem');await button('delete').trigger('click');confirmDelete();await flushPromises();expect(success).not.toHaveBeenCalled();expect(error).toHaveBeenCalledOnce();expect(config.list).toHaveBeenCalledOnce();});
    if(config.kind==='tag')it('编辑保留未展示的机器键、图标与描述',async()=>{await setup();await button('edit').trigger('click');submit();await flushPromises();expect(vi.mocked(config.update).mock.calls[0]?.slice(1,6)).toEqual(['旧目录','KEEP','book','#123456','保留描述']);});
    if(config.kind==='category')it.each([-2147483648,2147483647])('合法Int32边界%s完整提交',async value=>{await setup();await openCreate();await dialog().findAllComponents(ElInput)[1]!.setValue(String(value));submit();await flushPromises();expect(vi.mocked(config.create).mock.calls[0]?.[2]).toBe(value);});
    if(config.kind==='category')it.each(['1.5','1abc','1e3','2147483648','-2147483649'])('非法Int32排序%s不截断提交',async value=>{await setup();await openCreate();await dialog().findAllComponents(ElInput)[1]!.setValue(value);submit();await flushPromises();expect(config.create).not.toHaveBeenCalled();});
  });
}
