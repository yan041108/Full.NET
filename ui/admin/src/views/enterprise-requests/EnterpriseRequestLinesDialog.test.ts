import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { enterpriseRequestsHttp } from '../../api/enterprise-requests';
import { createOutputSession, deferred, outputId } from '../../test/data-output-fixtures';
import { useAdminI18n } from '../../i18n/adminI18n';
import Dialog from './EnterpriseRequestLinesDialog.vue';

vi.mock('../../api/enterprise-requests', async original => ({ ...await original<typeof import('../../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() } }));
const request = vi.mocked(enterpriseRequestsHttp.request);
const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const response = { requestId: outputId, requestVersion: '2', requestStatus: 'Draft', totalAmount: '50.01', items: [
  { id: nextId, lineNumber: 1, itemDescription: '最新项目', quantity: '1.0001', unitPrice: '50', lineAmount: '50.01' }
] };
const read = 'enterprise_request.enterprise_requests.read', update = 'enterprise_request.enterprise_requests.update';
function fixture(permissions = [read, update]) {
  const { pinia, session } = createOutputSession(permissions);
  return { session, wrapper: mount(Dialog, { props: { requestId: outputId }, global: { plugins: [pinia], stubs: { teleport: true } } }) };
}
async function click(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find(value => value.text() === text);
  expect(button, text).toBeDefined(); await button!.trigger('click');
}
describe('申请明细的版本与归属', () => {
  beforeEach(() => { useAdminI18n().setLocale('zh-CN'); request.mockReset(); request.mockResolvedValue(response); });
  it('读取精确数量金额、表头和当前主表版本', async () => {
    const f = fixture(); try { await flushPromises(); expect(f.wrapper.text()).toContain('最新项目'); expect(f.wrapper.text()).toContain('1.0001');
      expect(f.wrapper.find('caption').text()).toBe('申请明细'); expect(f.wrapper.findAll('th')).toHaveLength(5); expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it.each(['no_read', 'read_only', 'submitted'])('%s 不显示编辑或越权请求', async kind => {
    if (kind === 'submitted') request.mockResolvedValue({ ...response, requestStatus: 'Submitted' });
    const f = fixture(kind === 'no_read' ? [] : kind === 'read_only' ? [read] : [read, update]);
    try { await flushPromises(); expect(f.wrapper.findAll('button').some(button => button.text() === '编辑明细')).toBe(false);
      expect(request).toHaveBeenCalledTimes(kind === 'no_read' ? 0 : 1);
    } finally { f.wrapper.unmount(); }
  });
  it('修改行传递精确字符串和版本，成功接入新版本并通知列表刷新', async () => {
    const f = fixture(); try {
      await flushPromises(); await click(f.wrapper, '编辑明细');
      const inputs = f.wrapper.findAll('input'); await inputs[1]!.setValue('2');
      request.mockResolvedValueOnce({ ...response, requestVersion: '3', totalAmount: '100', items: [{ ...response.items[0]!, quantity: '2', lineAmount: '100' }] });
      await click(f.wrapper, '保存明细'); await flushPromises();
      const body = JSON.parse(request.mock.calls[1]![1]!.body as string);
      expect(body).toEqual({ version: 2, items: [{ itemDescription: '最新项目', quantity: '2', unitPrice: '50' }] });
      expect(f.wrapper.emitted('changed')).toHaveLength(1); expect(f.wrapper.findAll('input')).toHaveLength(0); expect(f.wrapper.text()).toContain('100');
    } finally { f.wrapper.unmount(); }
  });
  it('增删行、取消编辑均不发写请求', async () => {
    const f = fixture(); try { await flushPromises(); await click(f.wrapper, '编辑明细'); await click(f.wrapper, '添加明细');
      expect(f.wrapper.findAll('fieldset')).toHaveLength(2); await click(f.wrapper, '删除'); expect(f.wrapper.findAll('fieldset')).toHaveLength(1);
      await click(f.wrapper, '取消编辑'); expect(request).toHaveBeenCalledTimes(1); expect(f.wrapper.find('table').exists()).toBe(true);
    } finally { f.wrapper.unmount(); }
  });
  it('409 保留输入；取消编辑并刷新可接入最新快照', async () => {
    const f = fixture(); try { await flushPromises(); await click(f.wrapper, '编辑明细');
      request.mockRejectedValueOnce({ status: 409, code: 'version_conflict', title: '版本已变化' });
      await click(f.wrapper, '保存明细'); await flushPromises(); expect(f.wrapper.text()).toContain('版本已变化'); expect(f.wrapper.findAll('input')).toHaveLength(3);
      await click(f.wrapper, '取消编辑'); await click(f.wrapper, '刷新'); await flushPromises(); expect(f.wrapper.text()).not.toContain('版本已变化');
    } finally { f.wrapper.unmount(); }
  });
  it.each(['close', 'tenant', 'permission', 'unmount'])('%s 丢弃旧写响应并取消信号', async change => {
    const f = fixture(); const pending = deferred<unknown>();
    try { await flushPromises(); await click(f.wrapper, '编辑明细'); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '保存明细');
      if (change === 'close') await click(f.wrapper, '取消');
      else if (change === 'tenant') f.session.currentUser = { ...f.session.currentUser!, tenantId: nextId };
      else if (change === 'permission') f.session.currentUser = { ...f.session.currentUser!, permissions: [read] };
      else f.wrapper.unmount();
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve({ ...response, requestVersion: '3' }); await flushPromises(); expect(f.wrapper.emitted('changed')).toBeUndefined();
    } finally { if (change !== 'unmount') f.wrapper.unmount(); }
  });
  it('换单据时迟到读取不污染新详情或结束新加载', async () => {
    const old = deferred<unknown>(), next = deferred<unknown>(); request.mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise); const f = fixture();
    try { await flushPromises(); await f.wrapper.setProps({ requestId: nextId }); old.resolve(response); await flushPromises();
      expect(f.wrapper.text()).toContain('加载中'); expect(f.wrapper.text()).not.toContain('最新项目');
      next.resolve({ ...response, requestId: nextId, items: [] }); await flushPromises(); expect(f.wrapper.text()).toContain('暂无明细');
    } finally { f.wrapper.unmount(); }
  });
});
