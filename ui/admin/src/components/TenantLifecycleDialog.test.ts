import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElButton } from 'element-plus';
import { useSessionStore } from '../auth/session';
import TenantLifecycleDialog from './TenantLifecycleDialog.vue';
import * as api from '../api/tenant-lifecycle';

vi.mock('../api/tenant-lifecycle', () => ({ getTenantLifecycle: vi.fn(), changeTenantLifecycle: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showSuccess: vi.fn() }));
const id = '01981a3f-00c0-7000-8000-000000000010';
const row = { id, identifier: 'acme', domain: 'acme.localhost', name: 'Acme', isActive: true, version: 3,
  lifecycleStatus: 'Active' as const, provisioningStatus: 'Completed' };
let wrapper: ReturnType<typeof mount>;
function setup(actions: string[] = ['suspend', 'reactivate', 'close']) {
  const pinia = createPinia(); setActivePinia(pinia);
  useSessionStore().$patch({ state: 'authenticated', currentUser: { id, username: 'operator', displayName: 'Operator', actorScope: 'host', scope: 'host', tenantId: null,
    isSuperAdministrator: false, permissions: ['tenancy.tenant_lifecycle.read', ...actions.map(action => 'tenancy.tenant_lifecycle.' + action)],
    sessionId: id, preferredLocale: 'zh-CN', profileVersion: 1 } });
  wrapper = mount(TenantLifecycleDialog, { props: { open: true, tenantId: id }, global: { plugins: [pinia] }, attachTo: document.body });
}
async function click(label: string) {
  const button = wrapper.findAllComponents(ElButton).find(button => button.text() === label);
  expect(button, label).toBeDefined(); await button!.trigger('click'); await flushPromises();
}
beforeEach(() => { vi.clearAllMocks(); vi.mocked(api.getTenantLifecycle).mockResolvedValue(row); });
afterEach(() => { wrapper?.unmount(); });
it('读取权限只展示权威状态，不提供写操作', async () => {
  setup([]); await flushPromises();
  expect(document.body.textContent).toContain('当前权限仅允许查看');
  expect(wrapper.findAllComponents(ElButton).some(button => button.text() === '暂停企业')).toBe(false);
});
it('暂停必须显式确认并固定读取版本', async () => {
  vi.mocked(api.changeTenantLifecycle).mockResolvedValue({ ...row, lifecycleStatus: 'Suspended', isActive: false, version: 4 });
  setup(); await flushPromises(); await click('暂停企业');
  expect(api.changeTenantLifecycle).not.toHaveBeenCalled();
  await click('确认操作');
  expect(api.changeTenantLifecycle).toHaveBeenCalledWith(id, 3, 'suspend');
  expect(wrapper.emitted('changed')).toHaveLength(1);
});
it('取消确认不发出状态写入', async () => {
  setup(); await flushPromises(); await click('暂停企业'); await click('取消');
  expect(api.changeTenantLifecycle).not.toHaveBeenCalled();
});
it('关闭必须输入匹配的企业编码', async () => {
  setup(); await flushPromises(); await click('关闭企业');
  const confirm = () => wrapper.findAllComponents(ElButton).find(button => button.text() === '确认操作')!;
  expect(confirm().attributes('disabled')).toBeDefined();
  const input = document.querySelector('#tenant-lifecycle-identifier') as HTMLInputElement;
  input.value = 'another'; input.dispatchEvent(new Event('input', { bubbles: true })); await flushPromises();
  expect(confirm().attributes('disabled')).toBeDefined();
  input.value = 'acme'; input.dispatchEvent(new Event('input', { bubbles: true })); await flushPromises();
  expect(confirm().attributes('disabled')).toBeUndefined();
});
it('409 后显示最新状态并取消旧确认，不自动重放', async () => {
  vi.mocked(api.changeTenantLifecycle).mockRejectedValue({ status: 409, code: 'tenancy.version_conflict', title: 'Conflict' });
  setup(); await flushPromises(); await click('暂停企业');
  vi.mocked(api.getTenantLifecycle).mockResolvedValue({ ...row, lifecycleStatus: 'Suspended', isActive: false, version: 4 });
  await click('确认操作');
  expect(api.changeTenantLifecycle).toHaveBeenCalledOnce();
  expect(document.body.textContent).toContain('企业状态已变化');
  expect(wrapper.findAllComponents(ElButton).some(button => button.text() === '确认操作')).toBe(false);
});
it('切换目标后丢弃原企业的在途响应', async () => {
  let finish!: (value: typeof row) => void;
  vi.mocked(api.getTenantLifecycle).mockImplementationOnce(() => new Promise(resolve => { finish = resolve; }));
  setup(); await flushPromises();
  const nextId = '01981a3f-00c0-7000-8000-000000000011';
  vi.mocked(api.getTenantLifecycle).mockResolvedValue({ ...row, id: nextId, name: 'New tenant' });
  await wrapper.setProps({ tenantId: nextId }); await flushPromises();
  finish({ ...row, name: 'Old response' }); await flushPromises();
  expect(document.body.textContent).toContain('New tenant'); expect(document.body.textContent).not.toContain('Old response');
});
it('关闭终态不提供恢复或其他写入口', async () => {
  vi.mocked(api.getTenantLifecycle).mockResolvedValue({ ...row, lifecycleStatus: 'Closed', isActive: false });
  setup(); await flushPromises();
  expect(wrapper.findAllComponents(ElButton).map(button => button.text())).toEqual(['刷新状态']);
});
