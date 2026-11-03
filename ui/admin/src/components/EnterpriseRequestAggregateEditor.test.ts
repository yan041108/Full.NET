import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import Editor from './EnterpriseRequestAggregateEditor.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/enterprise-request-aggregate';
import { showProblem } from '../feedback/fullNetMessage';
import type { EnterpriseRequestAggregateResponse } from '../api/enterprise-request-client/index.generated';

vi.mock('../api/enterprise-request-aggregate', () => ({ getEnterpriseRequestAggregate: vi.fn(), createEnterpriseRequestAggregate: vi.fn(), updateEnterpriseRequestAggregate: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showWarning: vi.fn(), showSuccess: vi.fn() }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const master = { id, organizationUnitId: id, applicantUserId: id, requestNumber: 'REQ', title: 'Title', totalAmount: '9007199254740993.12', status: 'Draft', version: '9007199254740993' };
const aggregate = { master, details: [{ id, lineNumber: 1, itemDescription: 'Item', quantity: '1', unitPrice: '1.00', lineAmount: '1.00', version: '9007199254740995' }] } as EnterpriseRequestAggregateResponse;
const permissions = ['enterprise_request.enterprise_requests.read', 'enterprise_request.enterprise_requests.create',
  'enterprise_request.enterprise_requests.update', 'enterprise_request.enterprise_request_lines.read'];
const wrappers: VueWrapper[] = [];
async function render(requestId: string | null = id, allowed = permissions) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id, tenantId: id, sessionId: id, permissions: allowed } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Editor, { props: { modelValue: true, requestId: requestId ?? undefined }, global: { plugins: [pinia], stubs: {
    ElDialog: { template: '<div><slot /><slot name="footer" /></div>' }
  } } }); wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
beforeEach(() => { vi.mocked(api.getEnterpriseRequestAggregate).mockResolvedValue(aggregate); });
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.restoreAllMocks(); vi.clearAllMocks(); });

it('缺明细读取权限不读取或显示现有主从数据', async () => {
  const { wrapper } = await render(id, permissions.filter(key => !key.endsWith('lines.read')));
  expect(api.getEnterpriseRequestAggregate).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="aggregate-save"]').exists()).toBe(false);
});
it('提交后的单据只读，不能添加、删除或保存明细', async () => {
  vi.mocked(api.getEnterpriseRequestAggregate).mockResolvedValue({ ...aggregate, master: { ...aggregate.master, status: 'Submitted' } });
  const { wrapper } = await render(); expect(wrapper.text()).toContain('当前状态或权限仅允许查看');
  expect(wrapper.find('[data-testid="aggregate-save"]').exists()).toBe(false); expect(wrapper.find('[data-testid="aggregate-add-line"]').exists()).toBe(false);
});
it('保存使用原始主从版本和精确金额，冲突后保留用户输入', async () => {
  vi.mocked(api.updateEnterpriseRequestAggregate).mockRejectedValue({ status: 409, code: 'version.conflict', title: 'Changed elsewhere' });
  const { wrapper } = await render();
  await wrapper.get('[data-testid="aggregate-line-description"]').setValue('Changed');
  await wrapper.get('[data-testid="aggregate-save"]').trigger('click'); await flushPromises();
  const body = vi.mocked(api.updateEnterpriseRequestAggregate).mock.calls[0]![1];
  expect(body.master.version).toBe('9007199254740993'); expect(body.master.totalAmount).toBe(master.totalAmount);
  expect(body.updated[0]).toMatchObject({ id, version: '9007199254740995', input: { itemDescription: 'Changed' } });
  expect(showProblem).toHaveBeenCalled(); expect(wrapper.emitted('saved')).toBeUndefined();
  expect((wrapper.get('[data-testid="aggregate-line-description"]').element as HTMLInputElement).value).toBe('Changed');
});
it('新建主从单据只发送可写字段及明细，重复提交只调用一次', async () => {
  let resolve!: (value: EnterpriseRequestAggregateResponse) => void;
  vi.mocked(api.createEnterpriseRequestAggregate).mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper } = await render(null);
  await wrapper.get('[data-testid="aggregate-organization"]').setValue(id);
  await wrapper.get('[data-testid="aggregate-number"]').setValue('NEW'); await wrapper.get('[data-testid="aggregate-title"]').setValue('New');
  await wrapper.get('[data-testid="aggregate-add-line"]').trigger('click');
  await wrapper.get('[data-testid="aggregate-line-description"]').setValue('New line');
  await wrapper.get('[data-testid="aggregate-save"]').trigger('click'); await wrapper.get('form').trigger('submit');
  expect(api.createEnterpriseRequestAggregate).toHaveBeenCalledTimes(1);
  const [organization, body] = vi.mocked(api.createEnterpriseRequestAggregate).mock.calls[0]!;
  expect(organization).toBe(id); expect(body.master.status).toBe('Draft'); expect(body.details[0]?.itemDescription).toBe('New line');
  expect(JSON.stringify(body)).not.toContain('organizationUnitId'); expect(JSON.stringify(body)).not.toContain('version');
  resolve(aggregate); await flushPromises(); expect(wrapper.emitted('saved')).toHaveLength(1);
});
it('会话切换取消加载，旧主从数据不能重新出现', async () => {
  let resolve!: (value: EnterpriseRequestAggregateResponse) => void;
  vi.mocked(api.getEnterpriseRequestAggregate).mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper, session } = await render(); const signal = vi.mocked(api.getEnterpriseRequestAggregate).mock.calls[0]![1]!;
  session.switching = true; await flushPromises(); resolve(aggregate); await flushPromises();
  expect(signal.aborted).toBe(true); expect(wrapper.find('[data-testid="aggregate-line"]').exists()).toBe(false); expect(wrapper.emitted('saved')).toBeUndefined();
});
