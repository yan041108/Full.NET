import { mount, flushPromises } from '@vue/test-utils';
import { ElDialog } from 'element-plus';
import { watch } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EnterpriseRequestsView from './EnterpriseRequestsView.vue';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';

vi.mock('../api/enterprise-requests', async original => ({
  ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const row = { id: outputId, tenantId: outputId, organizationUnitId: outputId,
  requestNumber: 'REQ-1', title: '旧租户资料', status: 'Draft', totalAmount: 12,
  applicantUserId: outputId, version: 1, createdAtUtc: '2026-10-08T00:00:00Z',
  createdById: outputId, updatedAtUtc: null, updatedById: null, isDeleted: false,
  deletedAtUtc: null, deletedById: null };
const list = { items: [row], page: 1, pageSize: 20, total: 1 };
const permissions = ['read', 'create', 'update', 'disable'].map(x => 'enterprise_request.enterprise_requests.' + x);
function fixture(granted = permissions) {
  const { pinia, session } = createOutputSession(granted);
  const wrapper = mount(EnterpriseRequestsView, { global: { plugins: [pinia], stubs: { teleport: true } } });
  return { wrapper, session };
}
const click = async (wrapper: ReturnType<typeof mount>, text: string) => {
  const button = wrapper.findAll('button').find(b => b.text() === text);
  expect(button, text).toBeDefined(); await button!.trigger('click');
};

describe('企业样例对话框归属', () => {
  it.each(['Submitted', 'Approved', 'Rejected', 'Cancelled'])('状态 %s 没有编辑或删除入口', async status => {
    request.mockResolvedValue({ ...list, items: [{ ...row, status }] });
    const f = fixture();
    try {
      await flushPromises();
      const labels = f.wrapper.findAll('button').map(button => button.text());
      expect(labels).not.toContain('编辑'); expect(labels).not.toContain('删除');
    } finally { f.wrapper.unmount(); }
  });
  it('草稿状态字段只读，创建正文保持 Draft', async () => {
    const f = fixture();
    try {
      await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
      const field = f.wrapper.findComponent(ElDialog).findAll('.el-form-item')
        .find(item => item.text().includes('Status'))!;
      expect(field.find('input').attributes('disabled')).toBeDefined();
      await click(f.wrapper, '保存');
      expect(JSON.parse(request.mock.calls[1]![1]!.body as string)).toMatchObject({ status: 'Draft' });
    } finally { f.wrapper.unmount(); }
  });
  it.each([['update', false], ['submit', true]] as const)('只有 %s 时审批按钮可见性为 %s', async (action, visible) => {
    const f = fixture(['read', action].map(value => 'enterprise_request.enterprise_requests.' + value));
    try {
      await flushPromises();
      expect(f.wrapper.findAll('button').some(button => button.text() === '提交审批')).toBe(visible);
    } finally { f.wrapper.unmount(); }
  });
  beforeEach(() => { request.mockReset(); request.mockResolvedValue(list); });
  it('切换租户关闭对话框并清空表单，重新创建不得携带旧输入', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
    await f.wrapper.findComponent(ElDialog).get('input').setValue(outputId);
    f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf300' };
    await flushPromises();
    expect(f.wrapper.findComponent(ElDialog).props('modelValue')).toBe(false);
    await click(f.wrapper, '创建'); await flushPromises();
    expect((f.wrapper.findComponent(ElDialog).get('input').element as HTMLInputElement).value).toBe('');
    f.wrapper.unmount();
  });
  it('关闭创建弹窗取消写请求，迟到成功不得关闭后来重新打开的弹窗', async () => {
    const pending = deferred<unknown>(); const f = fixture(); await flushPromises();
    request.mockReturnValueOnce(pending.promise);
    await click(f.wrapper, '创建'); await flushPromises(); await click(f.wrapper, '保存');
    await click(f.wrapper, '取消');
    expect(request.mock.calls[1]![2]?.aborted).toBe(true);
    await click(f.wrapper, '创建'); await flushPromises();
    pending.resolve(row); await flushPromises();
    expect(f.wrapper.findComponent(ElDialog).props('modelValue')).toBe(true);
    expect(request).toHaveBeenCalledTimes(2); f.wrapper.unmount();
  });
  it('删除先展示确认，取消不得发送删除请求', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '删除'); await flushPromises();
    expect(request).toHaveBeenCalledTimes(1);
    await click(f.wrapper, '取消'); expect(request).toHaveBeenCalledTimes(1); f.wrapper.unmount();
  });
  it('模型成功与页面继续执行之间重开弹窗，旧成功不得关闭新弹窗', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
    request.mockResolvedValueOnce(row);
    const state = f.wrapper.vm as unknown as { changing: boolean };
    expect(state.changing).toBe(false);
    let reopened = false;
    const stop = watch(() => state.changing, (current, previous) => {
      if (previous && !current && !reopened) queueMicrotask(() => {
        reopened = true;
        f.wrapper.findAll('button').find(b => b.text() === '取消')!.element.click();
        f.wrapper.findAll('button').find(b => b.text() === '创建')!.element.click();
      });
    }, { flush: 'sync' });
    await click(f.wrapper, '保存'); await flushPromises();
    expect(reopened).toBe(true);
    expect(f.wrapper.findComponent(ElDialog).props('modelValue')).toBe(true);
    stop(); f.wrapper.unmount();
  });
});
