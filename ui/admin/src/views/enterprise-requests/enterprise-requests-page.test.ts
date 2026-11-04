import { expect, it, vi, describe } from 'vitest';
import { HttpClient, EnterpriseRequestResponse } from '@fullnet/client-contracts';
import { useEnterpriseRequestPage } from './enterprise-requests-page.generated';
import { mount, flushPromises } from '@vue/test-utils';
import { defineComponent, h, KeepAlive, ref } from 'vue';
import { deferred, outputId } from '../../test/data-output-fixtures';

const row: EnterpriseRequestResponse = { id: '019bc2b1-2a40-7cc3-8992-a80de51bf330', status: 'Draft',
  requestNumber: 'REQ-01', title: '申请单', totalAmount: '12', applicantUserId: outputId, tenantId: outputId,
  organizationUnitId: outputId, version: '1', createdAtUtc: '2026-10-17T00:00:00Z', createdById: outputId,
  updatedAtUtc: null, updatedById: null, isDeleted: false, deletedAtUtc: null, deletedById: null };

function create(permissions: string[]) {
  const request = vi.fn(); const problem = vi.fn();
  const page = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
    hasPermission: key => permissions.includes(key), onProblem: problem });
  return { page, request, problem };
}

it('编辑权限不授予提交审批权限，也不调用提交 API', async () => {
  const { page, request } = create(['enterprise_request.enterprise_requests.update']);
  expect(page.canUpdate.value).toBe(true); expect(page.canSubmit.value).toBe(false);
  expect(await page.submitForApproval(row)).toBe(false); expect(request).not.toHaveBeenCalled();
});

it('独立提交权限允许调用审批 API，拒绝畸形返回且恢复按钮状态', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.submit']);
  request.mockResolvedValue({ id: row.id });
  expect(page.canSubmit.value).toBe(true); expect(page.canUpdate.value).toBe(false);
  expect(await page.submitForApproval(row)).toBe(false);
  expect(request).toHaveBeenCalledWith(expect.stringContaining('/submit-for-approval'), { method: 'POST' }, expect.any(AbortSignal));
  expect(problem).toHaveBeenCalled(); expect(page.changing.value).toBe(false);
});

it('非草稿和重复点击不会重复提交审批', async () => {
  const { page, request } = create(['enterprise_request.enterprise_requests.submit']);
  expect(await page.submitForApproval({ ...row, status: 'Submitted' })).toBe(false);
  let reject!: (reason: Error) => void;
  request.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail; }));
  const pending = page.submitForApproval(row);
  expect(await page.submitForApproval(row)).toBe(false); expect(request).toHaveBeenCalledTimes(1);
  reject(new Error('network')); expect(await pending).toBe(false); expect(page.changing.value).toBe(false);
});

it('会话重置取消列表请求且旧租户迟到数据不能重新进入列表', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.read']);
  let resolve!: (value: unknown) => void;
  request.mockImplementation(() => new Promise(done => { resolve = done; }));
  const pending = page.load(); const signal = request.mock.calls[0]![2] as AbortSignal;
  page.reset(); expect(signal.aborted).toBe(true);
  resolve({ items: [row], page: 1, pageSize: 20, total: 1 });
  expect(await pending).toBe(false); expect(page.items.value).toEqual([]); expect(problem).not.toHaveBeenCalled();
});

it('会话重置后的旧写入失败不会覆盖新上下文错误或锁住新操作', async () => {
  const { page, request, problem } = create(['enterprise_request.enterprise_requests.submit']);
  let reject!: (reason: Error) => void;
  request.mockImplementation(() => new Promise((_resolve, fail) => { reject = fail; }));
  const pending = page.submitForApproval(row); const signal = request.mock.calls[0]![2] as AbortSignal;
  page.reset(); reject(new Error('old request')); expect(await pending).toBe(false);
  expect(signal.aborted).toBe(true); expect(problem).not.toHaveBeenCalled(); expect(page.changing.value).toBe(false);
});

const list = (title = row.title) => ({ items: [{ ...row, title }], page: 1, pageSize: 20, total: 1 });

function fixture(request: ReturnType<typeof vi.fn>) {
  const context = ref('tenant-a'); const allowed = ref(true); const onProblem = vi.fn();
  let model!: ReturnType<typeof useEnterpriseRequestPage>;
  const wrapper = mount(defineComponent({ setup() {
    model = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
      hasPermission: () => allowed.value, contextKey: () => context.value, onProblem });
    return () => h('div');
  } }));
  return { model, wrapper, context, allowed, onProblem };
}

describe('企业样例页面请求归属', () => {
  it.each(['Submitted', 'Approved', 'Rejected', 'Cancelled'])('状态 %s 禁止编辑和删除请求', async status => {
    const request = vi.fn().mockResolvedValue({ ...list(), items: [{ ...row, status }] });
    const f = fixture(request); await f.model.load(); const item = f.model.items.value[0]!;
    try {
      expect(await f.model.update(item, { requestNumber: 'REQ', title: 'changed', status: 'Draft', totalAmount: '1', applicantUserId: outputId })).toBe(false);
      expect(await f.model.remove(item)).toBe(false);
      expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it.each([['update', false], ['submit', true]] as const)('审批动作独立于 %s 权限', async (action, permitted) => {
    const request = vi.fn().mockResolvedValueOnce(list()).mockResolvedValueOnce({ ...row, status: 'Submitted' }).mockResolvedValue(list());
    let model!: ReturnType<typeof useEnterpriseRequestPage>;
    const permissions = ['read', action].map(value => 'enterprise_request.enterprise_requests.' + value);
    const wrapper = mount(defineComponent({ setup() {
      model = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
        hasPermission: permission => permissions.includes(permission), contextKey: () => 'tenant-a', onProblem: vi.fn() });
      return () => h('div');
    } }));
    try {
      await model.load();
      expect(await model.submitForApproval(model.items.value[0]!)).toBe(permitted);
      expect(request).toHaveBeenCalledTimes(permitted ? 3 : 1);
    } finally { wrapper.unmount(); }
  });

  it('只撤销审批权限也同步取消在途提交并丢弃迟到结果', async () => {
    const pending = deferred<unknown>(); const submitAllowed = ref(true);
    const request = vi.fn().mockResolvedValueOnce(list()).mockReturnValueOnce(pending.promise).mockResolvedValue(list());
    let model!: ReturnType<typeof useEnterpriseRequestPage>;
    const wrapper = mount(defineComponent({ setup() {
      model = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
        hasPermission: permission => permission !== 'enterprise_request.enterprise_requests.submit' || submitAllowed.value,
        contextKey: () => 'tenant-a', onProblem: vi.fn() });
      return () => h('div');
    } }));
    try {
      await model.load(); const old = model.submitForApproval(model.items.value[0]!);
      submitAllowed.value = false;
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      pending.resolve({ ...row, status: 'Submitted' });
      expect(await old).toBe(false);
    } finally { pending.resolve(row); wrapper.unmount(); }
  });
  it.each(['create', 'update', 'remove'] as const)('旧 %s 完成不得刷新新上下文', async action => {
    const pending = deferred<unknown>(); const request = vi.fn().mockResolvedValue(list());
    const f = fixture(request); await f.model.load(); const item = f.model.items.value[0]!;
    request.mockReturnValueOnce(pending.promise);
    const input = { organizationUnitId: outputId, requestNumber: 'REQ-new', title: '旧表单',
      status: 'Draft', totalAmount: '12', applicantUserId: outputId };
    const old = action === 'create' ? f.model.create(input) : action === 'update'
      ? f.model.update(item, input) : f.model.remove(item);
    f.context.value = 'tenant-b'; await flushPromises();
    pending.resolve(row); expect(await old).toBe(false);
    expect(request).toHaveBeenCalledTimes(3); expect(f.onProblem).not.toHaveBeenCalled(); f.wrapper.unmount();
  });

  it('KeepAlive 停用同步清除资料并阻止动作，重新激活只恢复一次读取', async () => {
    const show = ref(true); const request = vi.fn().mockResolvedValue(list());
    let model!: ReturnType<typeof useEnterpriseRequestPage>;
    const View = defineComponent({ name: 'ScopedEnterpriseFixture', setup() {
      model = useEnterpriseRequestPage({ request: { request } as unknown as HttpClient,
        hasPermission: () => true, contextKey: () => 'tenant-a', onProblem: vi.fn() });
      return () => h('div');
    } });
    const wrapper = mount(defineComponent({ setup: () => () => h(KeepAlive, () => show.value ? h(View) : null) }));
    await model.load(); const oldRow = model.items.value[0]!;
    show.value = false; await flushPromises();
    expect(model.items.value).toEqual([]); expect(await model.submitForApproval(oldRow)).toBe(false);
    expect(await model.load()).toBe(false); expect(request).toHaveBeenCalledTimes(1);
    show.value = true; await flushPromises();
    expect(request).toHaveBeenCalledTimes(2); expect(model.items.value[0]?.title).toBe(row.title); wrapper.unmount();
  });

  it('切换租户同步取消旧读取并自动恢复新读取，迟到结果不得覆盖新租户', async () => {
    const old = deferred<unknown>();
    const request = vi.fn().mockReturnValueOnce(old.promise).mockResolvedValue(list('新租户资料'));
    const f = fixture(request); const pending = f.model.load();
    f.context.value = 'tenant-b';
    expect(request.mock.calls[0]![2]?.aborted).toBe(true);
    await flushPromises();
    old.resolve(list());
    expect(await pending).toBe(false);
    expect(f.model.items.value[0]?.title).toBe('新租户资料');
    expect(f.onProblem).not.toHaveBeenCalled(); f.wrapper.unmount();
  });

  it('撤权同步清空已显示资料，拒绝旧行动作', async () => {
    const request = vi.fn().mockResolvedValue(list()); const f = fixture(request);
    await f.model.load(); const oldRow = f.model.items.value[0]!;
    f.allowed.value = false;
    expect(f.model.items.value).toEqual([]);
    expect(f.model.total.value).toBe(0);
    expect(await f.model.submitForApproval(oldRow)).toBe(false);
    expect(request).toHaveBeenCalledTimes(1); f.wrapper.unmount();
  });

  it('旧写入完成不能刷新新租户或结束新写入状态', async () => {
    const oldWrite = deferred<unknown>(); const newWrite = deferred<unknown>();
    const request = vi.fn().mockResolvedValueOnce(list()).mockReturnValueOnce(oldWrite.promise)
      .mockResolvedValueOnce(list('新租户资料')).mockReturnValueOnce(newWrite.promise).mockResolvedValue(list('新租户资料'));
    const f = fixture(request); await f.model.load();
    const old = f.model.submitForApproval(f.model.items.value[0]!);
    f.context.value = 'tenant-b'; await flushPromises();
    const current = f.model.submitForApproval(f.model.items.value[0]!);
    oldWrite.resolve({ ...row, status: 'Submitted' });
    expect(await old).toBe(false); expect(f.model.changing.value).toBe(true);
    expect(request).toHaveBeenCalledTimes(4);
    newWrite.resolve({ ...row, status: 'Submitted' }); expect(await current).toBe(true);
    f.wrapper.unmount();
  });

  it('卸载后取消请求并忽略迟到错误', async () => {
    const old = deferred<unknown>(); const request = vi.fn().mockReturnValue(old.promise);
    const f = fixture(request); const pending = f.model.load(); f.wrapper.unmount();
    expect(request.mock.calls[0]![2]?.aborted).toBe(true);
    old.reject(new Error('迟到错误'));
    expect(await pending).toBe(false); expect(f.onProblem).not.toHaveBeenCalled();
  });
});
