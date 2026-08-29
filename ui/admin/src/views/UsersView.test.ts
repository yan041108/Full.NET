import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { nextTick } from 'vue';
import UsersView from './UsersView.vue';
import { useSessionStore } from '../auth/session';
import {
  createHostUser,
  downloadHostUserImportTemplate,
  exportHostUsersWorkbook,
  getHostUserRoles,
  importHostUsersWorkbook,
  listHostUsers,
  replaceHostUserRoles,
  updateHostUser
} from '../api/users';
import { listHostRoles } from '../api/roles';
import {
  createHostUserOrganizationUnit,
  getHostUserOrganizationReference,
  updateHostUserOrganizationUnit
} from '../api/host-user-organization-reference';

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
  exportHostUsersWorkbook: vi.fn(),
  downloadHostUserImportTemplate: vi.fn(),
  importHostUsers: vi.fn(),
  importHostUsersWorkbook: vi.fn(),
  batchDisableHostUsers: vi.fn(),
  batchEnableHostUsers: vi.fn(),
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
const downloadTemplateMock = vi.mocked(downloadHostUserImportTemplate);
const exportWorkbookMock = vi.mocked(exportHostUsersWorkbook);
const importWorkbookMock = vi.mocked(importHostUsersWorkbook);
const updateUserMock = vi.mocked(updateHostUser);
const replaceRolesMock = vi.mocked(replaceHostUserRoles);
const listRolesMock = vi.mocked(listHostRoles);
const getUserRolesMock = vi.mocked(getHostUserRoles);
const getOrgReferenceMock = vi.mocked(getHostUserOrganizationReference);
const createUserUnitMock = vi.mocked(createHostUserOrganizationUnit);
const updateUserUnitMock = vi.mocked(updateHostUserOrganizationUnit);
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
const roleId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const orgUnitId = '019bc2b1-2a40-7cc3-8992-a80de51bf29a';
const orgUnitAssignmentId = '019bc2b1-2a40-7cc3-8992-a80de51bf29b';

const activeUser = {
  id: userId,
  username: 'active-user',
  displayName: '活动用户',
  accountType: 'normal_user',
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
const orgUnit = {
  id: orgUnitId,
  parentId: null,
  code: 'HQ',
  name: '总部',
  displayOrder: 1,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
};
const orgUnitAssignment = {
  id: orgUnitAssignmentId,
  userId,
  username: activeUser.username,
  displayName: activeUser.displayName,
  unitId: orgUnitId,
  unitCode: orgUnit.code,
  unitName: orgUnit.name,
  isPrimary: false,
  isActive: true,
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 1
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
    downloadTemplateMock.mockReset().mockResolvedValue(new Blob(['template']));
    exportWorkbookMock.mockReset().mockResolvedValue(new Blob(['export']));
    importWorkbookMock.mockReset().mockResolvedValue({
      succeededCount: 1,
      results: [
        {
          line: 2,
          succeeded: true,
          userId,
          errorCode: null,
          message: null
        },
        {
          line: 3,
          succeeded: false,
          userId: null,
          errorCode: 'identity.user.username_conflict',
          message: '用户名已存在。'
        }
      ]
    });
    updateUserMock.mockReset().mockResolvedValue({ ...activeUser, version: 2 });
    createUserUnitMock.mockReset().mockResolvedValue({
      ...orgUnitAssignment,
      isPrimary: true
    });
    updateUserUnitMock.mockReset().mockResolvedValue({
      ...orgUnitAssignment,
      isPrimary: true,
      version: 2
    });
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
    expect(wrapper.find('[data-testid="users-action-import"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-batch-disable"]').exists()).toBe(false);
    expect(wrapper.find('[data-testid="users-action-batch-enable"]').exists()).toBe(false);
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
        // 中文注释：HostUserProfile.emergencyContactRelation 与 C# 端档案字典字段对齐，取值为 null 或字典字符串
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
          emergencyContactRelation: null,
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
        // 中文注释：HostUserProfile.emergencyContactRelation 与 C# 端档案字典字段对齐，取值为 null 或字典字符串
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
          emergencyContactRelation: null,
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

  it('Host 目录仅有 identity.users.read 且已选租户时会加载组织参考数据', async () => {
    mountUsers(['identity.users.read']);
    await flushPromises();

    expect(getOrgReferenceMock).toHaveBeenCalledWith(orgTenantId);
  });

  it('Host 目录有 identity.users.update 时显示机构分配入口', async () => {
    const wrapper = mountUsers(['identity.users.read', 'identity.users.update']);
    await flushPromises();

    expect(wrapper.find('[data-testid="users-action-org-units"]').exists()).toBe(true);
    wrapper.unmount();
  });

  it.each([
    ['identity.users.create', 'users-action-create'],
    ['identity.users.update', 'users-action-edit'],
    ['identity.users.assign_roles', 'users-action-roles'],
    ['identity.users.reset_password', 'users-action-reset-password'],
    ['identity.users.disable', 'users-action-disable'],
    ['identity.users.export', 'users-action-export'],
    ['identity.users.import', 'users-action-import']
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
      'users-action-export',
      'users-action-import',
      'users-action-batch-disable',
      'users-action-batch-enable'
    ].filter(id => {
      if (id === testId) {
        return false;
      }
      if (permission === 'identity.users.disable' && id === 'users-action-batch-disable') {
        return false;
      }
      if (permission === 'identity.users.enable' && id === 'users-action-batch-enable') {
        return false;
      }
      return true;
    });
    for (const id of otherIds) {
      expect(wrapper.find(`[data-testid="${id}"]`).exists()).toBe(false);
    }

    // Host 目录持有 identity.users.read 且已选租户时，机构入口始终可见。
    expect(wrapper.find('[data-testid="users-action-org-units"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="users-action-org-positions"]').exists()).toBe(true);
  });

  it('导出、模板下载与工作簿导入均走受控文件端点', async () => {
    const createObjectUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:users');
    const revokeObjectUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.export',
      'identity.users.import'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-export"]').trigger('click');
    await flushPromises();
    expect(exportWorkbookMock).toHaveBeenCalledTimes(1);

    await wrapper.get('[data-testid="users-action-import-template"]').trigger('click');
    await flushPromises();
    expect(downloadTemplateMock).toHaveBeenCalledTimes(1);

    const input = wrapper.get('[data-testid="users-import-file-input"]');
    const file = new File(['workbook'], 'users.xlsx', { type: 'application/octet-stream' });
    Object.defineProperty(input.element, 'files', { configurable: true, value: [file] });
    await input.trigger('change');
    await flushPromises();
    expect(importWorkbookMock).toHaveBeenCalledWith(file);
    expect(wrapper.get('[data-testid="users-import-results"]').text())
      .toContain('identity.user.username_conflict');
    expect(wrapper.get('[data-testid="users-import-results"]').text())
      .toContain('用户名已存在。');

    createObjectUrl.mockRestore();
    revokeObjectUrl.mockRestore();
    wrapper.unmount();
  });

  it('仅授予启用权限时只对禁用用户显示启用按钮', async () => {
    const wrapper = mountUsers(['identity.users.read', 'identity.users.enable']);
    await flushPromises();

    expect(wrapper.find('[data-testid="users-action-enable"]').exists()).toBe(true);
    expect(wrapper.find('[data-testid="users-action-disable"]').exists()).toBe(false);
  });

  it('机构分配入口打开机构隶属页签', async () => {
    const wrapper = mountUsers([
      'identity.users.read',
      'organization.user_units.create'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-org-units"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.getComponent({ name: 'UserEditorDialog' });
    expect(dialog.props('activeTab')).toBe('org-units');
    wrapper.unmount();
  });

  it('职位分配入口打开职位隶属页签', async () => {
    const wrapper = mountUsers([
      'identity.users.read',
      'organization.user_positions.create'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-org-positions"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.getComponent({ name: 'UserEditorDialog' });
    expect(dialog.props('activeTab')).toBe('org-positions');
    wrapper.unmount();
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

  it('创建后组织同步失败时重试不会重复创建用户', async () => {
    createUserMock.mockResolvedValueOnce({
      ...activeUser,
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf29c',
      username: 'new-user',
      displayName: '新用户'
    });
    createUserUnitMock
      .mockRejectedValueOnce(new Error('client.organization_user_unit_failed'))
      .mockResolvedValueOnce({
        ...orgUnitAssignment,
        id: '019bc2b1-2a40-7cc3-8992-a80de51bf29d',
        userId: '019bc2b1-2a40-7cc3-8992-a80de51bf29c',
        username: 'new-user',
        displayName: '新用户',
        isPrimary: true
      });
    getOrgReferenceMock.mockResolvedValue({
      units: [orgUnit],
      positions: [],
      userUnits: [],
      userPositions: []
    });

    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.create',
      'organization.user_units.create'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-create"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.getComponent({ name: 'UserEditorDialog' });
    dialog.vm.$emit('update:username', 'new-user');
    dialog.vm.$emit('update:display-name', '新用户');
    dialog.vm.$emit('update:password', 'Password123!');
    dialog.vm.$emit('update:primary-unit-id', orgUnitId);
    await flushPromises();

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(createUserMock).toHaveBeenCalledTimes(1);
    expect(createUserUnitMock).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('用户基础信息已保存，但组织关系尚未完成');
    const progress = wrapper.get('[data-testid="users-submit-progress"]');
    expect(progress.text()).toContain('当前保存进度');
    expect(progress.text()).toContain('基础信息已完成');
    expect(progress.text()).toContain('机构隶属待完成');
    expect(dialog.props('activeTab')).toBe('org-units');

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(createUserMock).toHaveBeenCalledTimes(1);
    expect(createUserUnitMock).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });

  it('角色同步失败时会提示剩余步骤并从角色步骤继续重试', async () => {
    createUserMock.mockResolvedValueOnce({
      ...activeUser,
      id: '019bc2b1-2a40-7cc3-8992-a80de51bf29e',
      username: 'role-user',
      displayName: '角色用户'
    });
    getUserRolesMock.mockResolvedValue({
      userId: '019bc2b1-2a40-7cc3-8992-a80de51bf29e',
      roleIds: [],
      version: 1
    });
    replaceRolesMock
      .mockRejectedValueOnce(new Error('client.host_user_roles_failed'))
      .mockResolvedValueOnce({
        userId: '019bc2b1-2a40-7cc3-8992-a80de51bf29e',
        roleIds: [roleId],
        version: 2
      });

    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.create',
      'identity.users.assign_roles'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-create"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.getComponent({ name: 'UserEditorDialog' });
    dialog.vm.$emit('update:username', 'role-user');
    dialog.vm.$emit('update:display-name', '角色用户');
    dialog.vm.$emit('update:password', 'Password123!');
    dialog.vm.$emit('update:selected-role-ids', [roleId]);
    await flushPromises();

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(createUserMock).toHaveBeenCalledTimes(1);
    expect(replaceRolesMock).toHaveBeenCalledTimes(1);
    expect(wrapper.text()).toContain('用户和组织信息已保存，但角色尚未完成');
    const progress = wrapper.get('[data-testid="users-submit-progress"]');
    expect(progress.text()).toContain('当前保存进度');
    expect(progress.text()).toContain('基础信息已完成');
    expect(progress.text()).toContain('角色授权待完成');
    expect(dialog.props('activeTab')).toBe('roles');

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(createUserMock).toHaveBeenCalledTimes(1);
    expect(replaceRolesMock).toHaveBeenCalledTimes(2);
    wrapper.unmount();
  });

  it('更新后组织同步失败时重试不会重复提交用户更新', async () => {
    updateUserUnitMock
      .mockRejectedValueOnce(new Error('client.organization_user_unit_failed'))
      .mockResolvedValueOnce({
        ...orgUnitAssignment,
        isPrimary: true,
        version: 2
      });
    getOrgReferenceMock.mockResolvedValue({
      units: [orgUnit],
      positions: [],
      userUnits: [orgUnitAssignment],
      userPositions: []
    });

    const wrapper = mountUsers([
      'identity.users.read',
      'identity.users.update',
      'organization.user_units.update'
    ]);
    await flushPromises();

    await wrapper.get('[data-testid="users-action-edit"]').trigger('click');
    await flushPromises();

    const dialog = wrapper.getComponent({ name: 'UserEditorDialog' });
    dialog.vm.$emit('update:primary-unit-id', orgUnitId);
    await flushPromises();

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(updateUserMock).toHaveBeenCalledTimes(1);
    expect(updateUserUnitMock).toHaveBeenCalledTimes(1);

    dialog.vm.$emit('submit');
    await flushPromises();

    expect(updateUserMock).toHaveBeenCalledTimes(1);
    expect(updateUserUnitMock).toHaveBeenCalledTimes(2);
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
