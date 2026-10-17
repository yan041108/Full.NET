import { mount, flushPromises } from '@vue/test-utils';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { enterpriseRequestsHttp } from '../../api/enterprise-requests';
import { useAdminI18n } from '../../i18n/adminI18n';
import { createOutputSession, deferred, outputId } from '../../test/data-output-fixtures';
import Dialog from './EnterpriseRequestDetailDialog.vue';
import { requestStatusLabel, requestTime } from './enterprise-request-presentation';

vi.mock('../../api/enterprise-requests', async original => ({
  ...await original<typeof import('../../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const response = { id: outputId, tenantId: outputId, organizationUnitId: outputId,
  requestNumber: 'REQ-DETAIL', title: '最新申请内容', status: 'Draft', totalAmount: 12.5,
  applicantUserId: outputId, version: '2', createdAtUtc: '2026-10-08T00:00:00Z', createdById: outputId,
  updatedAtUtc: null, updatedById: null, isDeleted: false, deletedAtUtc: null, deletedById: null };
const permission = 'enterprise_request.enterprise_requests.read';
function fixture(permissions = [permission]) {
  const { pinia, session } = createOutputSession(permissions);
  return { session, wrapper: mount(Dialog, { props: { requestId: outputId }, global: { plugins: [pinia], stubs: { teleport: true } } }) };
}
async function click(wrapper: ReturnType<typeof mount>, text: string) {
  const button = wrapper.findAll('button').find(value => value.text() === text);
  expect(button).toBeDefined(); await button!.trigger('click');
}

describe('申请详情的读取与生命周期', () => {
  beforeEach(() => { useAdminI18n().setLocale('zh-CN'); request.mockReset(); request.mockResolvedValue(response); });
  it('显示服务端最新内容、状态、金额、版本与审计字段', async () => {
    const f = fixture();
    try {
      await flushPromises();
      expect(f.wrapper.text()).toContain('最新申请内容'); expect(f.wrapper.text()).toContain('草稿');
      expect(f.wrapper.text()).toContain('12.50'); expect(f.wrapper.text()).toContain('创建时间');
      expect(f.wrapper.find('dl').text()).toContain('2'); expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it('缺少读取权限不请求且刷新按钮不进入 DOM', async () => {
    const f = fixture([]);
    try {
      await flushPromises(); expect(request).not.toHaveBeenCalled(); expect(f.wrapper.text()).toContain('当前已无权');
      expect(f.wrapper.findAll('button').some(button => button.text() === '刷新')).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('精确十进制字符串保留原值，避免超出 Number 安全精度时改变金额', async () => {
    request.mockResolvedValue({ ...response, totalAmount: '9999999999999999.99' }); const f = fixture();
    try { await flushPromises(); expect(f.wrapper.text()).toContain('9999999999999999.99'); }
    finally { f.wrapper.unmount(); }
  });
  it('关闭立即清空并取消请求，迟到响应不能回显', async () => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const f = fixture();
    try {
      await flushPromises(); await click(f.wrapper, '取消');
      expect(request.mock.calls[0]![2]?.aborted).toBe(true); expect(f.wrapper.emitted('close')).toHaveLength(1);
      pending.resolve(response); await flushPromises(); expect(f.wrapper.text()).not.toContain('最新申请内容');
    } finally { f.wrapper.unmount(); }
  });
  it.each(['tenant', 'permission', 'unmount'])('%s 变化丢弃旧错误和详情', async change => {
    const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise); const f = fixture();
    try {
      await flushPromises();
      if (change === 'tenant') {
        request.mockResolvedValueOnce({ ...response, title: '新租户内容' });
        f.session.currentUser = { ...f.session.currentUser!, tenantId: nextId };
      } else if (change === 'permission') f.session.currentUser = { ...f.session.currentUser!, permissions: [] };
      else f.wrapper.unmount();
      expect(request.mock.calls[0]![2]?.aborted).toBe(true);
      pending.reject({ status: 403, code: 'old.scope.denied', title: '旧租户错误' }); await flushPromises();
      if (change !== 'unmount') { expect(f.wrapper.text()).not.toContain('旧租户错误'); expect(f.wrapper.text()).not.toContain('最新申请内容'); }
    } finally { if (change !== 'unmount') f.wrapper.unmount(); }
  });
  it('切换单据取消旧请求，旧 finally 不结束新加载', async () => {
    const old = deferred<unknown>(); const next = deferred<unknown>();
    request.mockReturnValueOnce(old.promise).mockReturnValueOnce(next.promise); const f = fixture();
    try {
      await flushPromises(); await f.wrapper.setProps({ requestId: nextId });
      expect(request.mock.calls[0]![2]?.aborted).toBe(true);
      old.resolve(response); await flushPromises(); expect(f.wrapper.text()).toContain('加载中');
      next.resolve({ ...response, id: nextId, title: '新单据' }); await flushPromises();
      expect(f.wrapper.text()).toContain('新单据'); expect(f.wrapper.text()).not.toContain('最新申请内容');
    } finally { f.wrapper.unmount(); }
  });
  it('刷新互斥；404 清空旧详情，之后可恢复', async () => {
    const f = fixture();
    try {
      await flushPromises(); const pending = deferred<unknown>(); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '刷新'); await click(f.wrapper, '刷新'); expect(request).toHaveBeenCalledTimes(2);
      expect(f.wrapper.text()).not.toContain('最新申请内容');
      pending.reject({ status: 404, code: 'enterprise_request.not_found', title: '申请已不可见' }); await flushPromises();
      expect(f.wrapper.text()).toContain('申请已不可见');
      await click(f.wrapper, '刷新'); await flushPromises(); expect(f.wrapper.text()).toContain('最新申请内容');
      expect(f.wrapper.text()).not.toContain('申请已不可见');
    } finally { f.wrapper.unmount(); }
  });
  it('错配响应失败关闭，不显示其他单据', async () => {
    request.mockResolvedValue({ ...response, id: nextId }); const f = fixture();
    try { await flushPromises(); expect(f.wrapper.text()).toContain('申请详情加载失败'); expect(f.wrapper.text()).not.toContain('最新申请内容'); }
    finally { f.wrapper.unmount(); }
  });
  it('语言切换即时更新显示，不改变原始业务标识或重发请求', async () => {
    const f = fixture();
    try {
      await flushPromises(); useAdminI18n().setLocale('en-US'); await flushPromises();
      expect(f.wrapper.text()).toContain('Request details'); expect(f.wrapper.text()).toContain('Draft');
      expect(f.wrapper.text()).toContain(outputId); expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); useAdminI18n().setLocale('zh-CN'); }
  });
  it('未知状态保留协议值；非法或空时间安全显示空占位', () => {
    expect(requestStatusLabel('FutureState', key => key)).toBe('FutureState');
    expect(requestTime(null, 'zh-CN')).toBe('—'); expect(requestTime('bad-date', 'zh-CN')).toBe('—');
  });
});
