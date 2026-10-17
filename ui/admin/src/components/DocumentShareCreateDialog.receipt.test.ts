import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import View from './DocumentShareCreateDialog.vue';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import * as shares from '../api/document-shares';
import { listDocumentItems } from '../api/host-document-items';
import * as feedback from '../feedback/fullNetMessage';
import { createShareSession, documentShare as share, shareDocument as doc, otherShareId } from '../test/document-share-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('../api/document-shares', () => ({ createDocumentShare: vi.fn(), batchCreateDocumentShares: vi.fn() }));
vi.mock('../api/host-document-items', () => ({ listDocumentItems: vi.fn() }));
const otherDoc = { ...doc, id: otherShareId, title: '失败文档', documentNo: 'DOC-2' };
const receipt = { succeededCount: 1, results: [
  { documentId: otherDoc.id, succeeded: false, share: null, errorCode: 'document.document_not_found', message: 'internal diagnostics' },
  { documentId: doc.id, succeeded: true, share, errorCode: null, message: null }
] };
const read = 'document.host_documents.read', create = 'document.host_shares.create', sharesRead = 'document.host_shares.read';
let wrapper: VueWrapper | undefined; let visible = ref(true);
const clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
beforeEach(() => {
  vi.resetAllMocks(); vi.mocked(shares.batchCreateDocumentShares).mockResolvedValue(receipt);
  vi.mocked(listDocumentItems).mockResolvedValue({ items: [doc], page: 1, pageSize: 100, total: 1 });
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: vi.fn().mockResolvedValue(undefined) } });
});
afterEach(() => { wrapper?.unmount(); wrapper = undefined; vi.restoreAllMocks();
  if (clipboardDescriptor) Object.defineProperty(navigator, 'clipboard', clipboardDescriptor); else Reflect.deleteProperty(navigator, 'clipboard'); });
async function setup(permissions = [read, create], keepAlive = false, tenantId: string | null = null) {
  const context = createShareSession(permissions); context.session.currentUser!.tenantId = tenantId; visible = ref(true);
  const props = { open: true, parentReadPermission: read as 'document.host_documents.read', presetDocuments: [doc, otherDoc] };
  const blank = defineComponent({ render: () => h('div') });
  const root = defineComponent({ setup: () => () => h(KeepAlive, null, { default: () => visible.value ? h(View, props) : h(blank) }) });
  const global = { plugins: [context.pinia], stubs: {
    ElDialog: { props: ['modelValue'], template: '<div v-if="modelValue"><slot/><slot name="footer"/></div>' }
  } };
  wrapper = keepAlive ? mount(root, { global }) : mount(View, { props, global }); await flushPromises(); return context.session;
}
function vm() { return (wrapper!.vm.$options.name === 'DocumentShareCreateDialog' ? wrapper!.vm : wrapper!.getComponent(View).vm) as unknown as {
  editorForm: { password: string }; submitCreate: () => Promise<void>; copyReceiptLink: (url: string) => Promise<void>;
}; }
function dialog() { return wrapper!.getComponent(ArtFormDialog); }
async function submit() { await vm().submitCreate(); await flushPromises(); }

describe('批量文档分享结果回执', () => {
  it('乱序结果按原文档展示成功链接与失败码，不自动关闭', async () => {
    await setup(); vm().editorForm.password = 'fixture-password'; await submit();
    expect(wrapper!.text()).toContain('失败文档'); expect(wrapper!.text()).toContain('document.document_not_found');
    expect(wrapper!.text()).not.toContain('internal diagnostics'); expect(wrapper!.find('[data-testid="document-share-batch-receipt"]').exists()).toBe(true);
    expect(dialog().props('open')).toBe(true); expect(wrapper!.emitted('batchCreated')).toEqual([[1, 2]]);
    expect(vm().editorForm.password).toBe(''); expect(dialog().props('showConfirm')).toBe(false);
  });
  it('已有回执不能再次提交整批，避免重复创建成功项', async () => {
    await setup(); await submit(); await submit(); expect(shares.batchCreateDocumentShares).toHaveBeenCalledOnce();
  });
  it('关闭结果后显式重开清空回执和密码', async () => {
    await setup(); await submit(); dialog().vm.$emit('update:open', false); await wrapper!.setProps({ open: false }); await wrapper!.setProps({ open: true });
    await flushPromises(); expect(wrapper!.find('[data-testid="document-share-batch-receipt"]').exists()).toBe(false); expect(vm().editorForm.password).toBe('');
    expect(dialog().props('showConfirm')).toBe(true);
  });
  it('只复制当前回执里的成功链接，不复制失败项或任意URL', async () => {
    await setup(); await submit(); await vm().copyReceiptLink('https://invalid.example/unknown'); expect(navigator.clipboard.writeText).not.toHaveBeenCalled();
    await wrapper!.get('[data-testid="document-share-receipt-copy"]').trigger('click'); await flushPromises(); expect(navigator.clipboard.writeText).toHaveBeenCalledOnce();
  });
  it('只有独立分享列表 Read 才显示显式列表入口', async () => {
    await setup(); await submit(); expect(wrapper!.find('[data-testid="document-share-receipt-view"]').exists()).toBe(false);
  });
  it('显式查看列表发出导航意图并关闭，不在回执到达时自动导航', async () => {
    await setup([read, create, sharesRead]); await submit(); expect(wrapper!.emitted('viewShares')).toBeUndefined();
    await wrapper!.get('[data-testid="document-share-receipt-view"]').trigger('click'); await flushPromises();
    expect(wrapper!.emitted('viewShares')).toHaveLength(1); expect(dialog().props('open')).toBe(false);
  });
  it.each(['resolve', 'reject'] as const)('复制等待时关闭，迟到%s不反馈', async outcome => {
    const pending = deferred<void>(); const success = vi.spyOn(feedback, 'showSuccess'); const error = vi.spyOn(feedback, 'showError');
    await setup(); await submit(); vi.mocked(navigator.clipboard.writeText).mockReturnValueOnce(pending.promise);
    await wrapper!.get('[data-testid="document-share-receipt-copy"]').trigger('click'); dialog().vm.$emit('update:open', false);
    if (outcome === 'resolve') pending.resolve(); else pending.reject(new Error('late')); await flushPromises();
    expect(success).not.toHaveBeenCalled(); expect(error).not.toHaveBeenCalled();
  });
  it('撤权立即清除回执链接，不能继续复制', async () => {
    const session = await setup(); await submit(); session.currentUser!.permissions = [read]; await flushPromises();
    expect(wrapper!.find('[data-testid="document-share-batch-receipt"]').exists()).toBe(false); expect(dialog().props('open')).toBe(false);
  });
  it('Host 文本作用域带非空租户不能创建', async () => {
    await setup([read, create], false, share.id); await submit(); expect(shares.batchCreateDocumentShares).not.toHaveBeenCalled(); expect(dialog().props('open')).toBe(false);
  });
  it('KeepAlive 停用回收结果，恢复不重放批量创建', async () => {
    await setup(undefined, true); await submit(); visible.value = false; await flushPromises(); visible.value = true; await flushPromises();
    expect(wrapper!.find('[data-testid="document-share-batch-receipt"]').exists()).toBe(false); expect(shares.batchCreateDocumentShares).toHaveBeenCalledOnce();
  });
  it('全失败保留每项结果，不显示任何可复制成功链接', async () => {
    vi.mocked(shares.batchCreateDocumentShares).mockResolvedValueOnce({ succeededCount: 0, results: receipt.results.map(value => ({ ...value, succeeded: false, share: null, errorCode: 'document.document_not_found' })) });
    await setup(); await submit(); expect(wrapper!.emitted('batchCreated')).toEqual([[0, 2]]); expect(wrapper!.findAll('[data-testid="document-share-receipt-copy"]')).toHaveLength(0);
  });
});
