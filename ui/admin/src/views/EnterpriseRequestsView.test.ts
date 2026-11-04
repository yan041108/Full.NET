import { mount, flushPromises } from '@vue/test-utils';
import { ElButton, ElDialog, ElDrawer, ElLoading } from 'element-plus';
import { reactive, watch } from 'vue';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import EnterpriseRequestsView from './EnterpriseRequestsView.vue';
import { enterpriseRequestsHttp } from '../api/enterprise-requests';
import { createOutputSession, deferred, outputId } from '../test/data-output-fixtures';
import { useAdminI18n } from '../i18n/adminI18n';

vi.mock('../api/enterprise-requests', async original => ({
  ...await original<typeof import('../api/enterprise-requests')>(), enterpriseRequestsHttp: { request: vi.fn() }
}));
const request = vi.mocked(enterpriseRequestsHttp.request);
const ownedReads=vi.hoisted(()=>({master:vi.fn(),attachments:vi.fn(),aggregate:vi.fn()}));
vi.mock('../api/enterprise-request-delivery',()=>({deliveryRequest:ownedReads.master,deliveryAttachments:ownedReads.attachments}));
vi.mock('../api/enterprise-request-aggregate',()=>({getEnterpriseRequestAggregate:ownedReads.aggregate,createEnterpriseRequestAggregate:vi.fn(),updateEnterpriseRequestAggregate:vi.fn()}));
beforeEach(()=>{Object.values(ownedReads).forEach(mock=>mock.mockReset());});
const route = reactive<{ name: string; query: Record<string, unknown> }>({ name: 'enterprise-requests', query: {} });
const replace = vi.fn(async (value: { query: Record<string, unknown> }) => { route.query = value.query; });
const push = vi.fn(async () => {});
vi.mock('vue-router', () => ({ useRoute: () => route, useRouter: () => ({ replace, push }) }));
const row = { id: outputId, tenantId: outputId, organizationUnitId: outputId,
  requestNumber: 'REQ-1', title: '旧租户资料', status: 'Draft', totalAmount: '12.00',
  applicantUserId: outputId, version: '1', createdAtUtc: '2026-10-08T00:00:00Z',
  createdById: outputId, updatedAtUtc: null, updatedById: null, isDeleted: false,
  deletedAtUtc: null, deletedById: null };
const list = { items: [row], page: 1, pageSize: 20, total: 1 };
const permissions = ['read', 'create', 'update', 'disable'].map(x => 'enterprise_request.enterprise_requests.' + x);
function fixture(granted = permissions) {
  const { pinia, session } = createOutputSession(granted);
  const wrapper = mount(EnterpriseRequestsView, { global: { plugins: [pinia], stubs: { teleport: true }, directives: { loading: ElLoading.directive } } });
  return { wrapper, session };
}
const click = async (wrapper: ReturnType<typeof mount>, text: string) => {
  const button = wrapper.findAll('button').find(b => b.text() === text);
  expect(button, text).toBeDefined(); await button!.trigger('click');
};
const createDialog = (wrapper: ReturnType<typeof mount>) => wrapper.findAllComponents(ElDialog)
  .find(dialog => dialog.props('title') === '创建')!;

describe('企业样例对话框归属', () => {
  it('业务链接可读取不在当前列表页的申请，关闭时只移除详情参数', async () => {
    const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
    route.query = { requestId: id, page: '3' };
    request.mockImplementation(async path => path.includes(id) ? { ...row, id, title: '链接申请' } : list);
    const f = fixture();
    try {
      await flushPromises(); expect(f.wrapper.text()).toContain('链接申请');
      expect(request.mock.calls.some(call => call[0] === `/api/v1/enterprise_request/enterprise-requests/${id}`)).toBe(true);
      const dialog = f.wrapper.findAllComponents(ElDrawer).find(value => value.props('title') === '申请详情')!;
      await dialog.findAll('button').find(value => value.text() === '关闭详情')!.trigger('click'); await flushPromises();
      expect(route.query).toEqual({ page: '3' });
    } finally { f.wrapper.unmount(); }
  });
  it.each(['bad', ['019bc2b1-2a40-7cc3-8992-a80de51bf330'], '00000000-0000-0000-0000-000000000000'])('非法详情参数 %s 不触发读取', async value => {
    route.query = { requestId: value }; const f = fixture();
    try { await flushPromises(); expect(request).toHaveBeenCalledTimes(1); }
    finally { f.wrapper.unmount(); }
  });
  it('链接没有权限不读取；租户变化关闭旧详情且不重新解释旧链接', async () => {
    route.query = { requestId: outputId }; const denied = fixture([]);
    await flushPromises(); expect(request).not.toHaveBeenCalled(); denied.wrapper.unmount();
    request.mockImplementation(async path => path.endsWith(outputId) ? row : list);
    const f = fixture();
    try {
      await flushPromises(); expect(f.wrapper.text()).toContain('申请详情');
      f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf330' };
      await flushPromises();
      expect(f.wrapper.findAllComponents(ElDrawer).some(value => value.props('title') === '申请详情'&&value.props('modelValue'))).toBe(false);
      expect(request.mock.calls.filter(call => call[0].endsWith(outputId))).toHaveLength(1);
      route.query = { ...route.query, page: '2' }; await flushPromises();
      expect(request.mock.calls.filter(call => call[0].endsWith(outputId))).toHaveLength(1);
    } finally { f.wrapper.unmount(); }
  });
  it('切换链接取消旧详情请求，迟到响应不能替换当前单据', async () => {
    const nextId = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
    const old = deferred<unknown>(); route.query = { requestId: outputId };
    request.mockImplementation(async path => path.endsWith(outputId) ? old.promise : path.endsWith(nextId) ? { ...row, id: nextId, title: '新链接申请' } : list);
    const f = fixture();
    try {
      await flushPromises(); const oldCall = request.mock.calls.find(call => call[0].endsWith(outputId))!;
      route.query = { requestId: nextId }; await flushPromises(); expect(oldCall[2]?.aborted).toBe(true);
      old.resolve({ ...row, title: '迟到旧详情' }); await flushPromises();
      expect(f.wrapper.text()).toContain('新链接申请'); expect(f.wrapper.text()).not.toContain('迟到旧详情');
      route.name = 'another-view'; await flushPromises();
      expect(f.wrapper.findAllComponents(ElDrawer).some(value => value.props('title') === '申请详情'&&value.props('modelValue'))).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('文档入口按独立附件权限重新读取原主单和受保护引用', async () => {
    const f = fixture([...permissions,'enterprise_request.attachments.read']);
    try {
      await flushPromises();
      ownedReads.master.mockResolvedValueOnce(row);ownedReads.attachments.mockResolvedValueOnce({items:[],page:1,pageSize:20,total:0});
      await click(f.wrapper,'附件与文档');await flushPromises();
      expect(ownedReads.master).toHaveBeenCalledExactlyOnceWith(outputId,expect.anything());
      expect(ownedReads.attachments).toHaveBeenCalledExactlyOnceWith(outputId,1,ownedReads.master.mock.calls[0]![1]);
      expect(f.wrapper.text()).toContain('申请交付');expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });
  it('主从入口按独立明细权限读取当前主单及字符串版本金额', async () => {
    const f = fixture([...permissions,'enterprise_request.enterprise_request_lines.read']);
    try {
      await flushPromises();
      ownedReads.aggregate.mockResolvedValueOnce({master:{...row,title:'权威主从标题',version:'2'},details:[]});
      await click(f.wrapper,'主从明细');await flushPromises();
      expect(ownedReads.aggregate).toHaveBeenCalledExactlyOnceWith(outputId,expect.anything());
      expect((f.wrapper.get('[data-testid="aggregate-title"]').element as HTMLInputElement).value).toBe('权威主从标题');
      expect((f.wrapper.get('[data-testid="aggregate-total"]').element as HTMLInputElement).value).toBe('12.00');expect(request).toHaveBeenCalledTimes(1);
    } finally { f.wrapper.unmount(); }
  });

  it('详情入口从服务端读取最新单据，不能直接展示列表快照', async () => {
    const f = fixture();
    try {
      await flushPromises(); request.mockResolvedValueOnce({ ...row, title: '最新资料', version: '2' });
      await click(f.wrapper, '详情'); await flushPromises();
      expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}`);
      expect(f.wrapper.findAllComponents(ElDrawer).find(dialog => dialog.props('title') === '申请详情')!.text()).toContain('最新资料');
    } finally { f.wrapper.unmount(); }
  });
  it('提交审批先确认，取消不会写入，确认正文不携带客户端状态', async () => {
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); await flushPromises();
      expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '取消'); expect(request).toHaveBeenCalledTimes(1);
      await click(f.wrapper, '提交审批'); request.mockResolvedValueOnce({ ...row, status: 'Submitted' });
      await click(f.wrapper, '确认提交'); await flushPromises();
      expect(request.mock.calls[1]![0]).toContain('/submit-for-approval');
      expect(request.mock.calls[1]![1]).toEqual({ method: 'POST' });
    } finally { f.wrapper.unmount(); }
  });
  it('撤销提交权限关闭确认框，旧确认动作不能继续提交', async () => {
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); await flushPromises();
      f.session.currentUser = { ...f.session.currentUser!, permissions: ['enterprise_request.enterprise_requests.read'] };
      await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).every(dialog => !dialog.props('modelValue'))).toBe(true);
      expect(request.mock.calls.some(call => call[0].endsWith('/submit-for-approval'))).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('关闭提交确认取消写请求，迟到错误不污染重新打开的确认框', async () => {
    const pending = deferred<unknown>();
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '确认提交'); await click(f.wrapper, '取消');
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      await click(f.wrapper,'刷新申请列表');await flushPromises();await click(f.wrapper, '提交审批');await flushPromises();
      expect(f.wrapper.get('[data-testid="enterprise-command-confirm"]').attributes('disabled')).toBeDefined();
      pending.reject({ status: 409, code: 'old.request.conflict', title: '旧提交失败' }); await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).find(dialog => dialog.props('title') === '确认提交审批')!.props('modelValue')).toBe(true);
      expect(f.wrapper.text()).not.toContain('旧提交失败'); expect(request).toHaveBeenCalledTimes(3);
    } finally { f.wrapper.unmount(); }
  });
  it('新提交处理中旧成功到达，不刷新列表、不关闭新确认或结束新加载态', async () => {
    const old = deferred<unknown>(); const next = deferred<unknown>();
    const f = fixture(['read', 'submit'].map(x => 'enterprise_request.enterprise_requests.' + x));
    try {
      await flushPromises(); await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(old.promise);
      await click(f.wrapper, '确认提交'); await click(f.wrapper, '取消');
      await click(f.wrapper,'刷新申请列表');await flushPromises();
      f.wrapper.findAllComponents({name:'ElPopconfirm'}).find(component=>component.props('title')?.includes('核对原申请'))!.vm.$emit('confirm');await flushPromises();
      await click(f.wrapper, '提交审批'); request.mockReturnValueOnce(next.promise); await click(f.wrapper, '确认提交');
      old.resolve({ ...row, status: 'Submitted' }); await flushPromises();
      expect(request).toHaveBeenCalledTimes(4);
      expect(f.wrapper.findAllComponents(ElDialog).find(dialog => dialog.props('title') === '确认提交审批')!.props('modelValue')).toBe(true);
      expect((f.wrapper.vm as unknown as { changing: boolean }).changing).toBe(true);
      next.resolve({ ...row, status: 'Submitted' }); await flushPromises();
      expect(request).toHaveBeenCalledTimes(5);
      expect((f.wrapper.vm as unknown as { changing: boolean }).changing).toBe(false);
    } finally { f.wrapper.unmount(); }
  });
  it('语言切换覆盖字段、状态与操作，机器标识保持原值', async () => {
    const f = fixture();
    try {
      await flushPromises(); useAdminI18n().setLocale('en-US'); await flushPromises();
      expect(f.wrapper.text()).toContain('Request number'); expect(f.wrapper.text()).toContain('Details');
      expect(f.wrapper.text()).toContain('Draft'); expect(f.wrapper.text()).toContain(outputId);
      expect(f.wrapper.text()).not.toContain('OrganizationUnitId'); expect(f.wrapper.text()).not.toContain('编辑');
    } finally { f.wrapper.unmount(); useAdminI18n().setLocale('zh-CN'); }
  });
  it.each(['Submitted', 'Approved'])('状态 %s 可查看审批进度，读取权限足够', async status => {
    request.mockResolvedValue({ ...list, items: [{ ...row, status }] });
    const f = fixture(['enterprise_request.enterprise_requests.read']);
    try {
      await flushPromises();
      expect(f.wrapper.findAll('button').map(button => button.text())).toContain('审批进度');
    } finally { f.wrapper.unmount(); }
  });
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
      const field = createDialog(f.wrapper).findAll('.el-form-item')
        .find(item => item.text().includes('单据状态'))!;
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
  beforeEach(() => { route.name = 'enterprise-requests'; route.query = {}; replace.mockClear(); request.mockReset(); request.mockResolvedValue(list); });
  it('实际点击进度读取当前行；切换租户关闭弹窗并取消旧查询', async () => {
    const pending = deferred<unknown>(); const f = fixture(['enterprise_request.enterprise_requests.read']);
    try {
      await flushPromises(); request.mockReturnValueOnce(pending.promise);
      await click(f.wrapper, '审批进度'); await flushPromises();
      expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}/approval-progress`);
      f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf300' };
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);
      await flushPromises();
      pending.resolve({ requestId: outputId }); await flushPromises();
      expect(f.wrapper.findAllComponents(ElDialog).every(dialog => !dialog.props('modelValue'))).toBe(true);
    } finally { f.wrapper.unmount(); }
  });
  it('切换租户关闭对话框并清空表单，重新创建不得携带旧输入', async () => {
    const f = fixture(); await flushPromises(); await click(f.wrapper, '创建'); await flushPromises();
    await createDialog(f.wrapper).get('input').setValue(outputId);
    f.session.currentUser = { ...f.session.currentUser!, tenantId: '019bc2b1-2a40-7cc3-8992-a80de51bf300' };
    await flushPromises();
    expect(createDialog(f.wrapper).props('modelValue')).toBe(false);
    await click(f.wrapper, '创建'); await flushPromises();
    expect((createDialog(f.wrapper).get('input').element as HTMLInputElement).value).toBe('');
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
    expect(createDialog(f.wrapper).props('modelValue')).toBe(true);
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
    expect(createDialog(f.wrapper).props('modelValue')).toBe(true);
    stop(); f.wrapper.unmount();
  });
  it('主单保存取消等待后锁定新保存，人工核对确认只解锁而不重发', async () => {
    const pending=deferred<unknown>(),f=fixture();
    try {
      await flushPromises();request.mockReturnValueOnce(pending.promise);await click(f.wrapper,'创建');await flushPromises();await click(f.wrapper,'保存');await click(f.wrapper,'取消');
      expect(request.mock.calls[1]![2]?.aborted).toBe(true);expect(f.wrapper.get('[data-testid="enterprise-editor-unconfirmed"]').text()).toContain('取消等待不代表服务端已回滚');
      await click(f.wrapper,'创建');await flushPromises();const save=createDialog(f.wrapper).findAll('button').find(button=>button.text()==='保存')!;expect(save.attributes('disabled')).toBeDefined();await save.trigger('click');expect(request).toHaveBeenCalledTimes(2);
      pending.resolve(row);await flushPromises();expect(createDialog(f.wrapper).props('modelValue')).toBe(true);expect(request).toHaveBeenCalledTimes(2);
      const recovery=f.wrapper.findAllComponents({name:'ElPopconfirm'}).find(component=>component.props('title')?.includes('核对原申请'))!;
      recovery.vm.$emit('confirm');await flushPromises();expect(save.attributes('disabled')).toBeUndefined();expect(request).toHaveBeenCalledTimes(2);
    } finally {f.wrapper.unmount();}
  });
  it('主单保存结果不明不能重复点击保存', async () => {
    const f=fixture();try {await flushPromises();request.mockRejectedValueOnce({status:503,code:'fixture.unknown',title:'未确认'});await click(f.wrapper,'创建');await flushPromises();await click(f.wrapper,'保存');await flushPromises();expect(f.wrapper.find('[data-testid="enterprise-editor-unconfirmed"]').exists()).toBe(true);await click(f.wrapper,'保存');expect(request).toHaveBeenCalledTimes(2);}finally{f.wrapper.unmount();}
  });
  it('主单保存旧按钮的保存和关闭回调不能操作新弹窗', async () => {
    const f=fixture();try {
      await flushPromises();await click(f.wrapper,'创建');await flushPromises();const buttons=createDialog(f.wrapper).findAllComponents(ElButton);
      const save=buttons.find(button=>button.text()==='保存')!.vm.$.vnode.props?.onClick as ()=>void;
      const close=buttons.find(button=>button.text()==='取消')!.vm.$.vnode.props?.onClick as ()=>void;
      expect(save).toBeTypeOf('function');expect(close).toBeTypeOf('function');close();await flushPromises();await click(f.wrapper,'创建');await flushPromises();save();close();await flushPromises();expect(request).toHaveBeenCalledTimes(1);expect(createDialog(f.wrapper).props('modelValue')).toBe(true);
    }finally{f.wrapper.unmount();}
  });
  it('主单保存核对回调失效且结果标记按原企业隔离', async () => {
    const f=fixture();try {
      await flushPromises();request.mockRejectedValueOnce({status:503,code:'fixture.unknown',title:'未确认'});await click(f.wrapper,'创建');await flushPromises();await click(f.wrapper,'保存');await flushPromises();
      const oldRecovery=f.wrapper.findAllComponents({name:'ElPopconfirm'}).find(component=>component.props('title')?.includes('核对原申请'))!.vm.$.vnode.props?.onConfirm as ()=>void;
      expect(oldRecovery).toBeTypeOf('function');await click(f.wrapper,'取消');oldRecovery();await flushPromises();expect(f.wrapper.find('[data-testid="enterprise-editor-unconfirmed"]').exists()).toBe(true);
      const original={...f.session.currentUser!};f.session.currentUser={...original,tenantId:'01980000-0000-7000-8000-000000000003'};await flushPromises();expect(f.wrapper.find('[data-testid="enterprise-editor-unconfirmed"]').exists()).toBe(false);
      f.session.currentUser=original;await flushPromises();expect(f.wrapper.find('[data-testid="enterprise-editor-unconfirmed"]').exists()).toBe(true);
    }finally{f.wrapper.unmount();}
  });
});

describe('申请摘要打印入口', () => {
  const read = 'enterprise_request.enterprise_requests.read';
  const printingRead = 'printing.published_templates.read';
  const printingPreview = 'printing.published_templates.preview';
  beforeEach(() => {
    route.name = 'enterprise-requests'; route.query = {};
    push.mockReset(); push.mockResolvedValue(undefined);
    request.mockReset(); request.mockResolvedValue(list);
  });
  it.each([{ extra: [] }, { extra: [printingRead] }, { extra: [printingPreview] }])('缺少完整打印能力不创建入口：%j', async ({ extra }) => {
    const f = fixture([read, ...extra]);
    try { await flushPromises(); expect(f.wrapper.find('[data-testid="enterprise-request-print"]').exists()).toBe(false); }
    finally { f.wrapper.unmount(); }
  });
  it('从当前可读行定位已授业务模板，不在列表内请求或触发打印', async () => {
    const f = fixture([read, printingRead, printingPreview]);
    try {
      await flushPromises();
      await f.wrapper.get('[data-testid="enterprise-request-print"]').trigger('click');
      expect(push).toHaveBeenCalledExactlyOnceWith({ name: 'printing-published-templates', query: {
        formSchemaKey: 'enterprise_request.request_summary', recordId: outputId
      } });
      expect(request).toHaveBeenCalledOnce();
    } finally { f.wrapper.unmount(); }
  });
  it('撤销打印权限移除入口，旧按钮引用也不能导航', async () => {
    const f = fixture([read, printingRead, printingPreview]);
    try {
      await flushPromises(); const button = f.wrapper.get('[data-testid="enterprise-request-print"]');
      f.session.currentUser = { ...f.session.currentUser!, permissions: [read, printingRead] };
      await flushPromises();
      expect(f.wrapper.find('[data-testid="enterprise-request-print"]').exists()).toBe(false);
      await button.trigger('click'); expect(push).not.toHaveBeenCalled();
    } finally { f.wrapper.unmount(); }
  });
  it.each(['permission', 'route', 'tenant'])('旧打印回调在 %s 边界恢复后仍失效', async boundary => {
    const f = fixture([read, printingRead, printingPreview]);
    try {
      await flushPromises();
      const button = f.wrapper.findAllComponents(ElButton).find(item => item.attributes('data-testid') === 'enterprise-request-print')!;
      const callback = button.vm.$.vnode.props?.onClick as () => void;
      expect(callback).toBeTypeOf('function');
      const original = { ...f.session.currentUser! };
      if (boundary === 'permission') f.session.currentUser = { ...original, permissions: [read] };
      else if (boundary === 'tenant') f.session.currentUser = { ...original, tenantId: '01980000-0000-7000-8000-000000000003' };
      else route.name = 'overview';
      await flushPromises(); f.session.currentUser = original; route.name = 'enterprise-requests'; await flushPromises();
      callback(); expect(push).not.toHaveBeenCalled();
    } finally { f.wrapper.unmount(); }
  });
});

describe('申请命令确认',()=>{
  const read='enterprise_request.enterprise_requests.read';
  beforeEach(()=>{route.name='enterprise-requests';route.query={};request.mockReset().mockResolvedValue(list);});
  const cases=[{action:'提交审批',permission:'submit',title:'确认提交审批',confirm:'确认提交'},
    {action:'删除',permission:'disable',title:'确认删除',confirm:'确认删除'}];
  it.each(cases)('$action 旧确认关闭后不能作用于新确认',async scenario=>{
    const f=fixture([read,'enterprise_request.enterprise_requests.'+scenario.permission]);try{
      await flushPromises();await click(f.wrapper,scenario.action);await flushPromises();
      const button=f.wrapper.findAllComponents(ElButton).find(item=>item.attributes('data-testid')==='enterprise-command-confirm')!;
      const callback=button.vm.$.vnode.props?.onClick as ()=>void;expect(callback).toBeTypeOf('function');await click(f.wrapper,'取消');await click(f.wrapper,scenario.action);callback();await flushPromises();
      expect(request).toHaveBeenCalledTimes(1);expect(f.wrapper.findAllComponents(ElDialog).some(dialog=>dialog.props('title')===scenario.title&&dialog.props('modelValue'))).toBe(true);
    }finally{f.wrapper.unmount();}
  });
  it.each(cases)('$action 撤权再恢复后旧确认仍失效',async scenario=>{
    const allowed=[read,'enterprise_request.enterprise_requests.'+scenario.permission],f=fixture(allowed);try{
      await flushPromises();await click(f.wrapper,scenario.action);await flushPromises();const callback=f.wrapper.findAllComponents(ElButton).find(item=>item.attributes('data-testid')==='enterprise-command-confirm')!.vm.$.vnode.props?.onClick as ()=>void;
      expect(callback).toBeTypeOf('function');f.session.currentUser={...f.session.currentUser!,permissions:[read]};await flushPromises();f.session.currentUser={...f.session.currentUser!,permissions:allowed};await flushPromises();callback();await flushPromises();expect(request.mock.calls.some(call=>call[1]?.method==='POST'||call[1]?.method==='DELETE')).toBe(false);
    }finally{f.wrapper.unmount();}
  });
  it('删除明确确认才发送原记录版本，未知结果禁止重发',async()=>{
    const f=fixture([read,'enterprise_request.enterprise_requests.disable']);try{
      await flushPromises();await click(f.wrapper,'删除');await flushPromises();expect(request).toHaveBeenCalledTimes(1);request.mockRejectedValueOnce({status:503,code:'fixture.unknown',title:'未知'});await click(f.wrapper,'确认删除');await flushPromises();expect(request.mock.calls[1]![1]?.method).toBe('POST');expect(request.mock.calls[1]![0]).toContain('/delete');expect(JSON.parse(request.mock.calls[1]![1]?.body as string)).toEqual({version:'1'});expect(f.wrapper.get('[data-testid="enterprise-command-confirm"]').attributes('disabled')).toBeDefined();await click(f.wrapper,'确认删除');expect(request).toHaveBeenCalledTimes(2);
    }finally{f.wrapper.unmount();}
  });
  it('确认期间列表换成新版本时拒绝旧目标并提示重新读取',async()=>{
    const f=fixture([read,'enterprise_request.enterprise_requests.submit']);try{
      await flushPromises();await click(f.wrapper,'提交审批');await flushPromises();request.mockResolvedValueOnce({...list,items:[{...row,version:'2'}]});await(f.wrapper.vm as unknown as {load:()=>Promise<boolean>}).load();await flushPromises();expect(f.wrapper.get('[data-testid="enterprise-command-confirm"]').attributes('disabled')).toBeDefined();expect(f.wrapper.text()).toContain('原记录已变化');expect(request).toHaveBeenCalledTimes(2);
    }finally{f.wrapper.unmount();}
  });
});

describe('申请读取入口',()=>{
  const read='enterprise_request.enterprise_requests.read';
  beforeEach(()=>{route.name='enterprise-requests';route.query={};request.mockReset().mockResolvedValue(list);});
  it('仅主单读取权限可从行重新读取摘要，不隐式加载明细或展示金额',async()=>{
    const f=fixture([read]);try{
      await flushPromises();request.mockResolvedValueOnce({...row,title:'当前权威摘要',totalAmount:'999.00'});await f.wrapper.get('[data-testid="enterprise-request-detail"]').trigger('click');await flushPromises();
      const drawer=f.wrapper.findComponent(ElDrawer);expect(drawer.text()).toContain('当前权威摘要');expect(drawer.text()).not.toContain('999.00');expect(request.mock.calls[1]![0]).toBe(`/api/v1/enterprise_request/enterprise-requests/${outputId}`);expect(request).toHaveBeenCalledTimes(2);
    }finally{f.wrapper.unmount();}
  });
  it.each(['permission','route','tenant'])('%s 边界恢复后旧详情按钮不能重新读取',async boundary=>{
    const f=fixture([read]);try{
      await flushPromises();const callback=f.wrapper.findAllComponents(ElButton).find(button=>button.attributes('data-testid')==='enterprise-request-detail')!.vm.$.vnode.props?.onClick as ()=>void;expect(callback).toBeTypeOf('function');
      const original={...f.session.currentUser!};if(boundary==='permission')f.session.currentUser={...original,permissions:[]};else if(boundary==='tenant')f.session.currentUser={...original,tenantId:'01980000-0000-7000-8000-000000000003'};else route.name='overview';await flushPromises();f.session.currentUser=original;route.name='enterprise-requests';await flushPromises();const reads=request.mock.calls.length;callback();await flushPromises();expect(request).toHaveBeenCalledTimes(reads);expect(f.wrapper.findComponent(ElDrawer).props('modelValue')).toBe(false);
    }finally{f.wrapper.unmount();}
  });
});
