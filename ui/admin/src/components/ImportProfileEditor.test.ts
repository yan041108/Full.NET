import { afterEach, beforeEach, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElSelect, ElPopconfirm } from 'element-plus';
import Editor from './ImportProfileEditor.vue';
import { useSessionStore } from '../auth/session';
import * as api from '../api/import-profiles';
vi.mock('../api/import-profiles', async original => ({ ...await original<typeof api>(), listImportProfiles: vi.fn(), getImportProfile: vi.fn(), saveImportProfile: vi.fn(), deleteImportProfile: vi.fn() }));
vi.mock('../feedback/fullNetMessage', () => ({ showProblem: vi.fn(), showSuccess: vi.fn() }));
const id = '019bc2b1-2a40-7cc3-8992-a80de51bf330';
const summary = { id, name: '供应商格式', headerFingerprint: 'a'.repeat(64), isCompatible: true, createdAtUtc: '2026-11-03T00:00:00Z', updatedAtUtc: null, version: 2 };
const configuration = { headerColumns: ['code', 'name'], columns: [{ targetColumn: 'code', sourceHeader: '编号' }, { targetColumn: 'name', sourceHeader: '名称' }] };
const wrappers: VueWrapper[] = [];
async function render(permissions = ['read', 'create', 'update', 'delete']) {
  const pinia = createPinia(); setActivePinia(pinia); const session = useSessionStore(pinia);
  session.currentUser = { id: 'owner', tenantId: 'tenant', sessionId: 'session', permissions: permissions.map(action => `import_export.import_profiles.${action}`) } as NonNullable<typeof session.currentUser>;
  const wrapper = mount(Editor, { props: { schemaKey: 'source', worksheetKey: 'rows', headers: ['code', 'name'], active: true }, global: { plugins: [pinia] } });
  wrappers.push(wrapper); await flushPromises(); return { wrapper, session };
}
async function enable(wrapper: VueWrapper) { await wrapper.get('input[type="checkbox"]').setValue(true); await flushPromises(); }
async function select(wrapper: VueWrapper) { wrapper.findComponent(ElSelect).vm.$emit('change', id); await flushPromises(); }
beforeEach(() => {
  vi.mocked(api.listImportProfiles).mockReset().mockResolvedValue([summary]); vi.mocked(api.getImportProfile).mockReset().mockResolvedValue({ summary, configuration });
  vi.mocked(api.saveImportProfile).mockReset().mockResolvedValue({ summary: { ...summary, version: 3 }, configuration }); vi.mocked(api.deleteImportProfile).mockReset().mockResolvedValue(undefined);
});
afterEach(() => { wrappers.splice(0).forEach(wrapper => wrapper.unmount()); vi.clearAllMocks(); });
it('恢复已保存映射，按版本更新并显示冻结约束', async () => {
  const { wrapper } = await render(); await enable(wrapper); await select(wrapper);
  expect(wrapper.get('[data-testid="import-profile-header-0"]').element).toHaveProperty('value', '编号');
  expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toEqual(configuration);
  await wrapper.get('[data-testid="import-profile-update"]').trigger('click'); await flushPromises();
  expect(api.saveImportProfile).toHaveBeenCalledWith('source', 'rows', id, '供应商格式', configuration, 2, expect.any(AbortSignal));
  expect(wrapper.text()).toContain('任务创建后列映射已冻结');
});
it('来源变化取消旧元数据并清空选中映射', async () => {
  let resolve!: (value: Awaited<ReturnType<typeof api.getImportProfile>>) => void;
  vi.mocked(api.getImportProfile).mockImplementation(() => new Promise(done => { resolve = done; }));
  const { wrapper } = await render(); await enable(wrapper); wrapper.findComponent(ElSelect).vm.$emit('change', id); await flushPromises();
  const signal = vi.mocked(api.getImportProfile).mock.lastCall?.[4]; await wrapper.setProps({ worksheetKey: 'other' });
  expect(signal?.aborted).toBe(true); resolve({ summary, configuration }); await flushPromises();
  expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toBeUndefined(); expect(wrapper.find('input[type="checkbox"]').element).toHaveProperty('checked', false);
});
it('未知写入结果阻止重放，显式读取最新版本后才允许更新', async () => {
  const { wrapper } = await render(); await enable(wrapper); await select(wrapper);
  vi.mocked(api.saveImportProfile).mockRejectedValueOnce(new Error('timeout'));
  await wrapper.get('[data-testid="import-profile-update"]').trigger('click'); await flushPromises();
  expect(wrapper.get('[data-testid="import-profile-update"]').attributes('disabled')).toBeDefined();
  await wrapper.get('[data-testid="import-profile-update"]').trigger('click'); expect(api.saveImportProfile).toHaveBeenCalledTimes(1);
  vi.mocked(api.getImportProfile).mockResolvedValue({ summary: { ...summary, version: 4 }, configuration });
  await wrapper.get('[data-testid="import-profile-reload"]').trigger('click'); await flushPromises();
  await wrapper.get('[data-testid="import-profile-update"]').trigger('click'); await flushPromises();
  expect(vi.mocked(api.saveImportProfile).mock.lastCall?.[5]).toBe(4);
});
it('失效配置可以修复，删除携带版本且不清除已编辑的映射', async () => {
  vi.mocked(api.getImportProfile).mockResolvedValue({ summary: { ...summary, isCompatible: false }, configuration: null });
  const { wrapper } = await render(); await enable(wrapper); await select(wrapper);
  expect(wrapper.text()).toContain('配置表头已变化');
  await wrapper.get('[data-testid="import-profile-header-0"]').setValue('编号');
  wrapper.findComponent(ElPopconfirm).vm.$emit('confirm'); await flushPromises();
  expect(api.deleteImportProfile).toHaveBeenCalledWith('source', 'rows', id, 2, expect.any(AbortSignal));
  expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ columns: [{ targetColumn: 'code', sourceHeader: '编号' }, { targetColumn: 'name', sourceHeader: 'name' }] });
});
it('无配置读取权限仍可显式编辑映射，不请求个人列表', async () => {
  const { wrapper } = await render([]); await enable(wrapper);
  expect(api.listImportProfiles).not.toHaveBeenCalled(); expect(wrapper.find('[data-testid="import-profile-save"]').exists()).toBe(false);
  await wrapper.get('[data-testid="import-profile-header-0"]').setValue('编号');
  expect(wrapper.emitted('update:modelValue')?.at(-1)?.[0]).toMatchObject({ columns: [{ targetColumn: 'code', sourceHeader: '编号' }, { targetColumn: 'name', sourceHeader: 'name' }] });
});
