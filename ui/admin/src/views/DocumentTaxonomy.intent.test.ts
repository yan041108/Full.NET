import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { defineComponent, KeepAlive, nextTick, ref } from 'vue';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { ElInput } from 'element-plus';
import Categories from './DocumentCategoriesView.vue';
import Tags from './DocumentTagsView.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import ArtTableActionButton from '../framework/art-design/components/ArtTableActionButton.vue';
import ArtTableHeader from '../framework/art-design/components/ArtTableHeader.vue';
import * as categoryApi from '../api/host-document-categories';
import * as tagApi from '../api/host-document-tags';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, shareId as id, otherShareId as other } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';

vi.mock('../api/host-document-categories', () => ({ listDocumentCategories: vi.fn(), createDocumentCategory: vi.fn(), updateDocumentCategory: vi.fn(), deleteDocumentCategory: vi.fn() }));
vi.mock('../api/host-document-tags', () => ({ listDocumentTags: vi.fn(), createDocumentTag: vi.fn(), updateDocumentTag: vi.fn(), deleteDocumentTag: vi.fn() }));
const common = { id, name: '原目录', code: 'KEEP', icon: 'book', color: '#123456', description: '原描述', createdAtUtc: '2026-10-10T00:00:00Z', updatedAtUtc: null, version: 1 };
const category = { ...common, parentId: other, sortOrder: 3 };
const tag = { ...common, useCount: 0, isHot: true, isRecommended: false };
const configs = [
  { kind: 'category', permission: 'document.categories', component: Categories, entity: category, list: categoryApi.listDocumentCategories, create: categoryApi.createDocumentCategory, update: categoryApi.updateDocumentCategory, remove: categoryApi.deleteDocumentCategory, listSignal: 0 },
  { kind: 'tag', permission: 'document.tags', component: Tags, entity: tag, list: tagApi.listDocumentTags, create: tagApi.createDocumentTag, update: tagApi.updateDocumentTag, remove: tagApi.deleteDocumentTag, listSignal: 1 }
];
let wrapper: VueWrapper | undefined;
beforeEach(() => vi.resetAllMocks());
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks(); });

for (const config of configs) {
  describe(config.kind + '目录目标与编辑意图', () => {
    const read = config.permission + '.read';
    const all = [read, config.permission + '.create', config.permission + '.update', config.permission + '.delete'];
    const button = (action: string) => wrapper!.get(`[data-testid="document-${config.kind}-${action}"]`);
    const editor = () => wrapper!.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === `document-${config.kind}-editor-submit`)!;
    const confirmation = () => wrapper!.findAllComponents(ArtFormDialog).find(item => item.props('confirmTestId') === `document-${config.kind}-delete-confirm`);
    const submit = () => editor().vm.$emit('confirm');
    async function refresh(rows: unknown[]) {
      vi.mocked(config.list).mockResolvedValueOnce(rows as never);
      wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh');
      await flushPromises();
    }
    async function setup(permissions = all, malformedHost = false, keepAlive = false) {
      const context = createShareSession(permissions);
      if (malformedHost) context.session.currentUser!.tenantId = other;
      vi.mocked(config.list).mockResolvedValue([{ ...config.entity }] as never);
      vi.mocked(config.create).mockResolvedValue({ ...config.entity } as never);
      vi.mocked(config.update).mockResolvedValue({ ...config.entity, version: 2 } as never);
      vi.mocked(config.remove).mockResolvedValue(true);
      const active = ref(true);
      const Host = defineComponent({ components: { KeepAlive, Page: config.component }, setup: () => ({ active }), template: '<KeepAlive><Page v-if="active"/></KeepAlive>' });
      wrapper = mount(keepAlive ? Host : config.component, { global: { plugins: [context.pinia], directives: { loading: () => {} }, stubs: { ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' } } } });
      await flushPromises();
      return { ...context, active };
    }
    it('Host字符串携带租户标识时不读取或开放操作', async () => {
      await setup(all, true); expect(config.list).not.toHaveBeenCalled(); expect(wrapper!.find('[data-route-heading]').exists()).toBe(false);
    });
    it.each(['missing', 'revision'] as const)('刷新后目标%s关闭编辑器且不提交旧版本', async change => {
      await setup(); await button('edit').trigger('click');
      await refresh(change === 'missing' ? [] : [{ ...config.entity, version: 2 }]);
      expect(editor().props('open')).toBe(false); submit(); await flushPromises(); expect(config.update).not.toHaveBeenCalled();
    });
    it('大小写相同UUID及相同修订刷新保留用户表单', async () => {
      await setup(); await button('edit').trigger('click'); await editor().getComponent(ElInput).setValue('我的修改');
      await refresh([{ ...config.entity, id: id.toUpperCase() }]);
      expect(editor().props('open')).toBe(true); expect(editor().getComponent(ElInput).props('modelValue')).toBe('我的修改');
      submit(); await flushPromises(); expect(vi.mocked(config.update).mock.calls[0]?.[0]).toBe(id); expect(vi.mocked(config.update).mock.calls[0]?.[1]).toBe('我的修改');
    });
    it('同修订刷新不能把隐藏字段偷换进已打开的编辑快照', async () => {
      await setup(); await button('edit').trigger('click');
      await refresh([{ ...config.entity, parentId: null, code: 'NEW', icon: 'new', color: '#ffffff', description: '新描述' }]);
      submit(); await flushPromises();
      const args = vi.mocked(config.update).mock.calls[0]!;
      if (config.kind === 'category') expect(args.slice(2, 9)).toEqual([other, 3, 'KEEP', 'book', '#123456', '原描述', 1]);
      else expect(args.slice(2, 9)).toEqual(['KEEP', 'book', '#123456', '原描述', 1, true, false]);
    });
    it('原目录对象就地升级不能把编辑快照的版本一起升级', async () => {
      await setup(); const edit = wrapper!.findAllComponents(ArtTableActionButton).find(item => item.props('testId') === `document-${config.kind}-edit`)!;
      // 模拟表格共享对象被更新，原表单不能自动借用更新后的修订提交旧内容。
      const table = wrapper!.getComponent({ name: 'ElTable' });
      const rows = table.props('data') as typeof config.entity[];
      const original = rows[0]!;
      edit.vm.$emit('click'); await nextTick();
      original.version = 2; await nextTick(); submit(); await flushPromises();
      expect(config.update).not.toHaveBeenCalled();
    });
    it.each(['edit', 'delete'] as const)('已移出目录行的迟到%s事件不能打开新操作', async action => {
      await setup();
      const control = wrapper!.findAllComponents(ArtTableActionButton).find(item => item.props('testId') === `document-${config.kind}-${action}`)!;
      const queued = control.vm.$.vnode.props!.onClick as () => void;
      await refresh([]); queued(); await flushPromises();
      expect(editor().props('open')).toBe(false); expect(confirmation()).toBeUndefined();
      expect(config.update).not.toHaveBeenCalled(); expect(config.remove).not.toHaveBeenCalled();
    });
    it.each(['missing', 'revision'] as const)('删除确认期间目标%s拒绝发送旧删除', async change => {
      await setup(); const success = vi.spyOn(feedback, 'showSuccess'); await button('delete').trigger('click');
      const accept = confirmation()!.vm.$.vnode.props!.onConfirm as () => void;
      await refresh(change === 'missing' ? [] : [{ ...config.entity, version: 2 }]);
      accept(); await flushPromises(); expect(config.remove).not.toHaveBeenCalled(); expect(success).not.toHaveBeenCalled();
    });
    it('取消旧确认后迟到事件不能批准新确认', async () => {
      await setup(); await button('delete').trigger('click'); const old = confirmation()!;
      // 捕获已经入队的监听器；卸载组件的 $emit 会自行短路，不能覆盖迟到回调归属。
      const accept = old.vm.$.vnode.props!.onConfirm as () => void;
      const cancel = old.vm.$.vnode.props!['onUpdate:open'] as (open: boolean) => void;
      old.vm.$emit('update:open', false); await flushPromises();
      await button('delete').trigger('click'); expect(confirmation()).toBeDefined();
      accept(); cancel(false); await flushPromises(); expect(config.remove).not.toHaveBeenCalled(); expect(confirmation()).toBeDefined();
      confirmation()!.vm.$emit('confirm'); await flushPromises(); expect(config.remove).toHaveBeenCalledExactlyOnceWith(id, 1, expect.any(AbortSignal));
    });
    it('打开创建会清空上一编辑表单且不能带入原分类父级', async () => {
      await setup(); await button('edit').trigger('click'); editor().vm.$emit('update:open', false); await nextTick();
      await button('create').trigger('click'); expect(editor().getComponent(ElInput).props('modelValue')).toBe('');
      await editor().getComponent(ElInput).setValue('新目录'); submit(); await flushPromises(); expect(config.create).toHaveBeenCalledOnce();
      if (config.kind === 'category') expect(vi.mocked(config.create).mock.calls[0]?.[1]).toBeNull();
    });
    it('原编辑被刷新取消后旧结果不影响新创建', async () => {
      await setup(); const old = deferred<typeof config.entity>(); vi.mocked(config.update).mockReturnValueOnce(old.promise as never);
      const success = vi.spyOn(feedback, 'showSuccess'); await button('edit').trigger('click'); submit();
      const signal = vi.mocked(config.update).mock.calls[0]?.[9] as AbortSignal;
      await refresh([{ ...config.entity, version: 2 }]); expect(signal.aborted).toBe(true);
      await button('create').trigger('click'); await editor().getComponent(ElInput).setValue('下一表单');
      old.resolve(config.entity); await flushPromises(); expect(success).not.toHaveBeenCalled();
      expect(editor().props('open')).toBe(true); expect(editor().getComponent(ElInput).props('modelValue')).toBe('下一表单');
    });
    it('确认成功删除后列表重读失败也不再展示已确认删除行', async () => {
      await setup(); vi.mocked(config.list).mockRejectedValueOnce(new Error('offline'));
      await button('delete').trigger('click'); confirmation()!.vm.$emit('confirm'); await flushPromises();
      expect(wrapper!.text()).not.toContain('原目录'); expect(wrapper!.getComponent({ name: 'ElTable' }).props('data')).toEqual([]); expect(config.remove).toHaveBeenCalledOnce();
    });
    it('更新冲突关闭旧快照并只重读不自动重写', async () => {
      await setup(); vi.mocked(config.update).mockRejectedValueOnce({ status: 409, code: 'document.version_conflict', title: 'Conflict' });
      await button('edit').trigger('click'); submit(); await flushPromises();
      expect(editor().props('open')).toBe(false); expect(config.update).toHaveBeenCalledOnce(); expect(config.list).toHaveBeenCalledTimes(2);
    });
    it('仅有页面读取权限不能创建编辑或删除', async () => {
      await setup([read]); for (const action of ['create', 'edit', 'delete']) expect(wrapper!.find(`[data-testid="document-${config.kind}-${action}"]`).exists()).toBe(false);
    });
    it('KeepAlive停用清空目录编辑器，旧结果不恢复；激活只重读', async () => {
      const { active } = await setup(all, false, true); await button('edit').trigger('click');
      const old = deferred<typeof config.entity[]>(); vi.mocked(config.list).mockReturnValueOnce(old.promise as never);
      wrapper!.getComponent(ArtTableHeader).vm.$emit('refresh'); await nextTick(); const signal = vi.mocked(config.list).mock.calls.at(-1)?.[config.listSignal] as AbortSignal;
      active.value = false; await nextTick(); expect(signal.aborted).toBe(true); old.resolve([{ ...config.entity, name: '迟到目录' }]); await flushPromises(); expect(wrapper!.text()).not.toContain('迟到目录');
      active.value = true; await flushPromises(); expect(editor().props('open')).toBe(false); expect(config.list).toHaveBeenCalledTimes(3); expect(config.update).not.toHaveBeenCalled();
    });
  });
}
