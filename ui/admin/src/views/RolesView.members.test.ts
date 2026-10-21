import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount, type VueWrapper } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { ElCheckboxGroup, ElPagination } from 'element-plus';
import RolesView from './RolesView.vue';
import { useSessionStore } from '../auth/session';
import { listHostRoleMembers, listHostRoles, replaceHostRoleMembers } from '../api/roles';
import { listHostUsers } from '../api/users';

vi.mock('../api/roles', () => ({
  createHostRole: vi.fn(), copyHostRole: vi.fn(), deleteHostRole: vi.fn(), disableHostRole: vi.fn(),
  enableHostRole: vi.fn(), getAuthorizationTree: vi.fn(), getFieldProjectionCatalog: vi.fn(),
  getHostRoleDataScope: vi.fn(), getHostRoleFieldGrants: vi.fn(), listHostRoleMembers: vi.fn(),
  listHostRoles: vi.fn(), replaceHostRoleMembers: vi.fn(), replaceHostRolePermissions: vi.fn(),
  replaceHostRoleFieldGrants: vi.fn(), updateHostRole: vi.fn(), updateHostRoleDataScope: vi.fn()
}));
vi.mock('../api/users', () => ({ listHostUsers: vi.fn() }));

const usersMock = vi.mocked(listHostUsers);
const membersMock = vi.mocked(listHostRoleMembers);
const replaceMock = vi.mocked(replaceHostRoleMembers);
const role = {
  id: 'role-support', code: 'support', name: '支持角色', isSystem: false, isActive: true,
  isSuperAdministrator: false, permissionCodes: [], createdAtUtc: '2026-10-20T00:00:00Z',
  updatedAtUtc: null, version: 3
};
const user = (id: string) => ({ id, username: id, displayName: id, isActive: true });
let wrapper: VueWrapper | undefined;

async function open() {
  const pinia = createPinia(); setActivePinia(pinia);
  useSessionStore().currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296', username: 'admin', displayName: '管理员',
    tenantId: null, actorScope: 'host', scope: 'host', isSuperAdministrator: false,
    passwordChangeRequired: false, permissions: ['identity.roles.read', 'identity.roles.replace_members'],
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297', preferredLocale: 'zh-CN', profileVersion: 1
  };
  wrapper = mount(RolesView, { global: {
    plugins: [pinia], directives: { loading: () => undefined },
    // 此处验证候选编辑契约；展开操作布局但保留真实 PermissionGate 和表单控件。
    stubs: { ArtTableActionGroup: { template: '<div><slot /></div>' } }
  } });
  await flushPromises();
  await wrapper.get('[data-testid="roles-action-members"]').trigger('click');
  await flushPromises();
  return wrapper;
}

function candidates(page: number) {
  const pagination = wrapper!.findAllComponents(ElPagination)
    .find(component => component.props('layout') === 'prev, pager, next');
  expect(pagination).toBeDefined();
  pagination!.vm.$emit('update:current-page', page);
}

describe('角色成员候选页部分保存', () => {
  beforeEach(() => {
    vi.mocked(listHostRoles).mockReset().mockResolvedValue({ items: [role], page: 1, pageSize: 20, total: 1 });
    usersMock.mockReset().mockImplementation(async (page = 1) => ({
      items: [user(page === 1 ? 'candidate-a' : 'candidate-b')], page, pageSize: 20, total: 60
    } as never));
    membersMock.mockReset().mockImplementation(async (_id, _page, _size, _signal, ids) => ({
      items: ids?.includes('candidate-a') ? [{ userId: 'candidate-a' }] : [],
      page: 1, pageSize: 100, total: 2, version: 11
    } as never));
    replaceMock.mockReset().mockResolvedValue({ roleId: role.id, userIds: [], version: 12 } as never);
  });
  afterEach(() => { wrapper?.unmount(); wrapper = undefined; });

  it('跨已访问页保留编辑，只提交加载过的候选和成员快照版本', async () => {
    const page = await open();
    expect(page.getComponent(ElCheckboxGroup).props('modelValue')).toEqual(['candidate-a']);
    page.getComponent(ElCheckboxGroup).vm.$emit('update:modelValue', []);
    candidates(2); await flushPromises();
    page.getComponent(ElCheckboxGroup).vm.$emit('update:modelValue', ['candidate-b']);
    candidates(1); await flushPromises();
    // 重访第一页不能用服务端原成员重新勾上用户已取消的 A；未加载成员 C 不在替换范围。
    expect(page.getComponent(ElCheckboxGroup).props('modelValue')).toEqual(['candidate-b']);
    expect(membersMock.mock.calls.map(call => call[4])).toEqual([
      ['candidate-a'], ['candidate-b'], ['candidate-a']
    ]);
    await page.get('[data-testid="roles-save-members"]').trigger('click'); await flushPromises();
    expect(replaceMock).toHaveBeenCalledExactlyOnceWith(role.id, ['candidate-b'], 11, undefined,
      ['candidate-a', 'candidate-b']);
  });

  it('翻页取消旧请求，晚到候选不能混入保存范围', async () => {
    const old = Promise.withResolvers<Awaited<ReturnType<typeof listHostUsers>>>();
    usersMock.mockImplementation(async (page = 1) => {
      if (page === 2) return old.promise;
      return { items: [user(page === 1 ? 'candidate-a' : 'candidate-d')], page, pageSize: 20, total: 60 } as never;
    });
    const page = await open();
    candidates(2); await flushPromises();
    const oldSignal = usersMock.mock.calls[1]![2]!;
    candidates(3); await flushPromises();
    expect(oldSignal.aborted).toBe(true);
    old.resolve({ items: [user('candidate-b')], page: 2, pageSize: 20, total: 60 } as never);
    await flushPromises();
    expect(page.getComponent(ElCheckboxGroup).text()).toContain('candidate-d');
    expect(page.getComponent(ElCheckboxGroup).text()).not.toContain('candidate-b');
    await page.get('[data-testid="roles-save-members"]').trigger('click'); await flushPromises();
    expect(replaceMock).toHaveBeenCalledExactlyOnceWith(role.id, ['candidate-a'], 11, undefined,
      ['candidate-a', 'candidate-d']);
  });
});
