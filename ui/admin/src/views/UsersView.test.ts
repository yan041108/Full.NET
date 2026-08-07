import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { nextTick } from 'vue';
import UsersView from './UsersView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostUser,
  getHostUserRoles,
  listHostUsers,
  replaceHostUserRoles,
  updateHostUser
} from '../api/users';
import { listHostRoles } from '../api/roles';
import { getHostUserOrganizationReference } from '../api/host-user-organization-reference';

vi.mock('../api/host-user-organization-reference', () => ({
  getHostUserOrganizationReference: vi.fn(),
  updateHostUserOrganizationPosition: vi.fn(),
  disableHostUserOrganizationPosition: vi.fn(),
  createHostUserOrganizationUnit: vi.fn(),
  updateHostUserOrganizationUnit: vi.fn(),
  disableHostUserOrganizationUnit: vi.fn(),
  createHostUserOrganizationPosition: vi.fn()
}));

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

const listUsersMock = vi.mocked(listHostUsers);
const createUserMock = vi.mocked(createHostUser);
const updateUserMock = vi.mocked(updateHostUser);
const replaceRolesMock = vi.mocked(replaceHostUserRoles);
const listRolesMock = vi.mocked(listHostRoles);
const getUserRolesMock = vi.mocked(getHostUserRoles);
const getOrgReferenceMock = vi.mocked(getHostUserOrganizationReference);
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
const roleId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';

const activeUser = {
  id: userId,
  username: 'active-user',
  displayName: '活动用户',
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1,
  projectedFields: {
    effectiveFieldKeys: [
      'id',
      'username',
      'display_name',
      'is_active',
      'created_at_utc',
      'updated_at_utc',
      'version'
    ],
    preferredLocale: null,
    failedLoginCount: null,
    lockoutEndUtc: null
  }
};

const inactiveUser = {
  ...activeUser,
  id: '019bc2b1-2a40-7cc3-8992-a80de51bf295',
  username: 'inactive-user',
  displayName: '禁用用户',
  isActive: false
};

const orgTenantId = '019bc2b1-2a40-7cc3-8992-a80de51bf299';

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
  session.availableTenants = [{
    id: orgTenantId,
    identifier: 'local',
    name: 'Full.NET Local',
    domain: 'localhost'
  }];
  return mount(UsersView, { global: { plugins: [pinia] } });
}

describe('Vue 用户管理页', () => {
  beforeEach(() => {
    createUserMock.mockReset();
    updateUserMock.mockReset().mockResolvedValue({ ...activeUser, version: 2 });
    replaceRolesMock.mockReset().mockResolvedValue({
      userId,
      roleIds: [roleId],
      version: 2
    });
    listUsersMock.mockReset().mockResolvedValue({
      items: [activeUser, inactiveUser],
      page: 1,
      pageSize: 100,
      total: 2
    });
    listRolesMock.mockReset().mockResolvedValue({
      items: [{
        id: roleId,
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
      roleIds: [roleId],
      version: 1
    });
    getOrgReferenceMock.mockReset().mockResolvedValue({
      units: [],
      positions: [],
      userUnits: [],
      userPositions: []
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

  it('无字段授权时即使服务端意外带回档案值也不在页面泄露', async () => {
    listUsersMock.mockResolvedValueOnce({
      items: [{
        ...activeUser,
        profile: {
          nickname: '密级昵称',
          phoneNumber: '13800000000',
          email: 'hidden@example.com',
          employeeNumber: 'E-007',
          gender: null,
          joinDateUtc: null,
          sortOrder: 9,
          idCardType: null,
          idCardNumber: null,
          birthDate: null,
          ethnicity: null,
          address: '隐藏地址',
          graduatedSchool: null,
          educationLevel: null,
          politicalStatus: null,
          officePhone: null,
          emergencyContact: null,
          emergencyContactPhone: null,
          emergencyContactAddress: null,
          remark: '隐藏备注',
          version: 2
        }
      }],
      page: 1,
      pageSize: 100,
      total: 1
    });

    const wrapper = mountUsers(['identity.users.read']);
    await flushPromises();

    expect(wrapper.text()).not.toContain('13800000000');
    expect(wrapper.text()).not.toContain('hidden@example.com');
    expect(wrapper.text()).not.toContain('E-007');
  });

  it('字段授权来自 effectiveFieldKeys，而不是超级管理员兜底', async () => {
    listUsersMock.mockResolvedValueOnce({
      items: [{
        ...activeUser,
        profile: {
          nickname: '可见昵称',
          phoneNumber: '13800000000',
          email: 'visible@example.com',
          employeeNumber: 'E-008',
          gender: null,
          joinDateUtc: null,
          sortOrder: null,
          idCardType: null,
          idCardNumber: null,
          birthDate: '2026-08-01',
          ethnicity: null,
          address: '可见地址',
          graduatedSchool: null,
          educationLevel: null,
          politicalStatus: null,
          officePhone: null,
          emergencyContact: null,
          emergencyContactPhone: null,
          emergencyContactAddress: null,
          remark: null,
          version: 3
        },
        projectedFields: {
          effectiveFieldKeys: [
            'id',
            'username',
            'display_name',
            'is_active',
            'created_at_utc',
            'updated_at_utc',
            'version',
            'nickname',
            'phone_number',
            'email',
            'employee_number',
            'birth_date',
            'address',
            'preferred_locale'
          ],
          preferredLocale: 'zh-CN',
          failedLoginCount: null,
          lockoutEndUtc: null
        }
      }],
      page: 1,
      pageSize: 100,
      total: 1
    });

    const wrapper = mountUsers(['identity.users.read', 'identity.users.update']);
    await flushPromises();

    expect(wrapper.text()).toContain('13800000000');
    expect(wrapper.text()).toContain('E-008');

    await wrapper.get('[data-testid="users-action-edit"]').trigger('click');
    await flushPromises();

    wrapper.unmount();
  });

  it('仅授予职位禁用权限时仍会加载组织参考数据', async () => {
    mountUsers([
      'identity.users.read',
      'organization.user_positions.disable'
    ]);
    await flushPromises();

    expect(getOrgReferenceMock).toHaveBeenCalledWith(orgTenantId);
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

  it('修改资料且角色未变时不得调用角色替换，避免误吊销会话', async () => {
    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.update',
      'identity.users.assign_roles'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-edit"]').trigger('click');
    await flushPromises();

    const submit = document.querySelector(
      '[data-testid="users-editor-submit"]'
    ) as HTMLButtonElement;
    expect(submit).not.toBeNull();
    await submit.click();
    await flushPromises();

    expect(updateUserMock).toHaveBeenCalledTimes(1);
    expect(replaceRolesMock).not.toHaveBeenCalled();
    wrapper.unmount();
  });

  it('角色页签无改动保存时也不得调用角色替换', async () => {
    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.assign_roles'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-roles"]').trigger('click');
    await flushPromises();

    const submit = document.querySelector(
      '[data-testid="users-editor-submit"]'
    ) as HTMLButtonElement;
    expect(submit).not.toBeNull();
    await submit.click();
    await flushPromises();

    expect(replaceRolesMock).not.toHaveBeenCalled();
    expect(updateUserMock).not.toHaveBeenCalled();
    wrapper.unmount();
  });
});
