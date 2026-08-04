import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { nextTick } from 'vue';
import UsersView from './UsersView.vue';
import { useSessionStore } from '../auth/session';
import { createHostUser, getHostUserRoles, listHostUsers } from '../api/users';
import { listHostRoles } from '../api/roles';
import { listOrganizationUnits } from '../api/org-units';
import { listOrganizationUserUnits } from '../api/org-user-units';
import { listOrganizationUserPositions } from '../api/org-user-positions';
import { listOrganizationPositions } from '../api/org-positions';

vi.mock('../api/users', () => ({
  createHostUser: vi.fn(),
  disableHostUser: vi.fn(),
  enableHostUser: vi.fn(),
  exportHostUsers: vi.fn(),
  getHostUserRoles: vi.fn(),
  listHostUsers: vi.fn(),
  replaceHostUserRoles: vi.fn(),
  resetHostUserPassword: vi.fn(),
  updateHostUser: vi.fn()
}));

vi.mock('../api/roles', () => ({
  createHostRole: vi.fn(),
  disableHostRole: vi.fn(),
  getAuthorizationTree: vi.fn(),
  getFieldProjectionCatalog: vi.fn(),
  getHostRoleDataScope: vi.fn(),
  getHostRoleFieldGrants: vi.fn(),
  listHostRoles: vi.fn(),
  replaceHostRolePermissions: vi.fn(),
  replaceHostRoleFieldGrants: vi.fn(),
  updateHostRole: vi.fn(),
  updateHostRoleDataScope: vi.fn()
}));

vi.mock('../api/org-units', () => ({
  listOrganizationUnits: vi.fn()
}));

vi.mock('../api/org-positions', () => ({
  listOrganizationPositions: vi.fn()
}));

vi.mock('../api/org-user-units', () => ({
  listOrganizationUserUnits: vi.fn()
}));

vi.mock('../api/org-user-positions', () => ({
  listOrganizationUserPositions: vi.fn()
}));

const listUsersMock = vi.mocked(listHostUsers);
const createUserMock = vi.mocked(createHostUser);
const listRolesMock = vi.mocked(listHostRoles);
const getUserRolesMock = vi.mocked(getHostUserRoles);
const listOrgUnitsMock = vi.mocked(listOrganizationUnits);
const listUserUnitsMock = vi.mocked(listOrganizationUserUnits);
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';

const activeUser = {
  id: userId,
  username: 'active-user',
  displayName: '活动用户',
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};

const inactiveUser = {
  ...activeUser,
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  username: 'inactive-user',
  displayName: '禁用用户',
  isActive: false
};

function mountUsers(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: '019bc2b1-2a40-7cc3-8992-a80de51bf296',
    username: 'admin',
    displayName: '管理员',
    tenantId: null,
    actorScope: 'host',
    scope: 'host',
    isSuperAdministrator: false,
    permissions,
    sessionId: '019bc2b1-2a40-7cc3-8992-a80de51bf297',
    preferredLocale: 'zh-CN',
    profileVersion: 1
  };
  return mount(UsersView, { global: { plugins: [pinia] } });
}

describe('Vue 用户管理页', () => {
  beforeEach(() => {
    createUserMock.mockReset();
    listUsersMock.mockReset().mockResolvedValue({
      items: [activeUser, inactiveUser],
      page: 1,
      pageSize: 100,
      total: 2
    });
    listRolesMock.mockReset().mockResolvedValue({
      items: [{
        id: 'role-id',
        code: 'support',
        name: '支持角色',
        isSystem: false,
        isActive: true,
        isSuperAdministrator: false,
        permissionCodes: [],
        createdAtUtc: '2026-07-21T00:00:00Z',
        updatedAtUtc: null,
        version: 1
      }],
      page: 1,
      pageSize: 200,
      total: 1
    });
    getUserRolesMock.mockReset().mockResolvedValue({
      userId,
      roleIds: [],
      version: 1
    });
    listOrgUnitsMock.mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 200,
      total: 0
    });
    listUserUnitsMock.mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 500,
      total: 0
    });
    vi.mocked(listOrganizationPositions).mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 200,
      total: 0
    });
    vi.mocked(listOrganizationUserPositions).mockReset().mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 500,
      total: 0
    });
  });

  it('只读用户可见目录但无业务操作控件', async () => {
    const wrapper = mountUsers(['identity.users.read']);
    await flushPromises();

    expect(wrapper.text()).toContain('活动用户');
    expect(wrapper.find('[data-testid="users-action-create"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-export"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-edit"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-roles"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-reset-password"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-disable"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-enable"]').exists()).toBe(false);
  });

  it.each([
    ['identity.users.create', 'users-action-create'],
    ['identity.users.update', 'users-action-edit'],
    ['identity.users.assign_roles', 'users-action-roles'],
    ['identity.users.reset_password', 'users-action-reset-password'],
    ['identity.users.disable', 'users-action-disable'],
    ['identity.users.export', 'users-action-export']
  ])('仅授予 %s 时只暴露对应控件', async (permission, testId) => {
    const wrapper = mountUsers(['identity.users.read', permission]);
    await flushPromises();

    expect(wrapper.find(`[data-testid="${testId}"]`).exists()).toBe(true);
    const otherIds = [
      'users-action-create',
      'users-action-edit',
      'users-action-roles',
      'users-action-reset-password',
      'users-action-disable',
      'users-action-enable',
      'users-action-export'
    ].filter(id => id !== testId);
    for (const id of otherIds) {
      expect(wrapper.find(`[data-testid="${id}"]`).exists()).toBe(false);
    }
  });

  it('仅授予启用权限时只对禁用用户显示启用按钮', async () => {
    const wrapper = mountUsers(['identity.users.read', 'identity.users.enable']);
    await flushPromises();

    expect(wrapper.find('[data-testid="users-action-enable"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="users-action-disable"]').exists()).toBe(false);
  });

  it('角色授权入口打开编辑弹窗并保留取消控件', async () => {
    const wrapper = mountUsers(['identity.users.read', 'identity.users.assign_roles']);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-roles"]').trigger('click');
    await flushPromises();

    expect(document.querySelector('[data-testid="users-editor-submit"]')).not.toBeNull();
    expect(document.body.textContent).toContain('取消');
    wrapper.unmount();
  });

  it('创建用户时缺少必填项会阻止提交', async () => {
    const wrapper = mountUsers(['identity.users.read', 'identity.users.create']);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-create"]').trigger('click');
    await flushPromises();
    await nextTick();

    const submit = document.querySelector('[data-testid="users-editor-submit"]') as HTMLButtonElement;
    expect(submit).not.toBeNull();
    await submit.click();
    await flushPromises();
    await nextTick();

    expect(createUserMock).not.toHaveBeenCalled();
    wrapper.unmount();
  });
});
