import { beforeEach, describe, expect, it, vi } from 'vitest';
import { flushPromises, mount } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { nextTick } from 'vue';
import RolesView from './RolesView.vue';
import { useSessionStore } from '../auth/session';
import {
  applyPermissionNodeCheck,
  buildPermissionTreeNodes,
  collectCatalogPermissionCodes,
  findUnknownPermissionCodes,
  permissionCodesToCheckedNodeIds
} from '../auth/authorization-tree-selection';
import {
  getAuthorizationTree,
  getHostRoleDataScope,
  listHostRoles,
  replaceHostRolePermissions
} from '../api/roles';

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

const listMock = vi.mocked(listHostRoles);
const treeMock = vi.mocked(getAuthorizationTree);
const dataScopeMock = vi.mocked(getHostRoleDataScope);
const replacePermissionsMock = vi.mocked(replaceHostRolePermissions);
const userId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';

const sampleTree = [
  {
    id: 'identity',
    title: '身份与权限',
    order: 10,
    pages: [
      {
        id: 'users',
        title: '用户管理',
        permissionCode: 'identity.users.read',
        order: 10,
        actions: [
          {
            id: 'identity.users.create',
            name: '创建用户',
            permissionCode: 'identity.users.create',
            order: 10
          },
          {
            id: 'identity.users.reset-password',
            name: '重置密码',
            permissionCode: 'identity.users.reset_password',
            order: 50
          }
        ],
        children: []
      }
    ]
  }
];

const customRole = {
  id: 'role-id',
  code: 'support',
  name: '支持角色',
  isSystem: false,
  isActive: true,
  isSuperAdministrator: false,
  permissionCodes: ['identity.users.read'],
  createdAtUtc: '2026-07-21T00:00:00Z',
  updatedAtUtc: null,
  version: 3
};

function mountWithPermissions(permissions: string[]) {
  const pinia = createPinia();
  setActivePinia(pinia);
  const session = useSessionStore();
  session.currentUser = {
    id: userId,
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
  return mount(RolesView, { global: { plugins: [pinia] } });
}

describe('角色授权树选择规则', () => {
  const nodes = buildPermissionTreeNodes(sampleTree);
  const pageNode = nodes[0]!.children![0]!;
  const createAction = pageNode.children?.[0]!;
  const resetAction = pageNode.children?.[1]!;

  it('勾选操作会自动包含页面权限', () => {
    const next = applyPermissionNodeCheck(new Set<string>(), createAction, true);
    expect([...next].sort()).toEqual([
      'identity.users.create',
      'identity.users.read'
    ]);
  });

  it('仅勾选页面时只保留页面读取权限', () => {
    const next = applyPermissionNodeCheck(new Set<string>(), pageNode, true);
    expect([...next]).toEqual(['identity.users.read']);
  });

  it('取消页面会清除全部后代操作权限', () => {
    const selected = new Set([
      'identity.users.read',
      'identity.users.create',
      'identity.users.reset_password'
    ]);
    const next = applyPermissionNodeCheck(selected, pageNode, false);
    expect([...next]).toEqual([]);
  });

  it('识别目录外未知权限并映射勾选节点', () => {
    const catalog = collectCatalogPermissionCodes(sampleTree);
    expect(findUnknownPermissionCodes(['identity.users.read', 'legacy.unknown'], catalog))
      .toEqual(['legacy.unknown']);
    const checked = permissionCodesToCheckedNodeIds(
      new Set(['identity.users.read', 'identity.users.create']),
      nodes
    );
    expect(checked).toEqual(['page:users', 'action:identity.users.create']);
  });
});

describe('Vue 角色管理页', () => {
  beforeEach(() => {
    listMock.mockReset().mockResolvedValue({
      items: [customRole],
      page: 1,
      pageSize: 20,
      total: 1
    });
    treeMock.mockReset().mockResolvedValue(sampleTree);
    dataScopeMock.mockReset().mockResolvedValue({
      roleId: customRole.id,
      dataScopeKind: 'identity.data_scope.all',
      unitIds: [],
      version: customRole.version
    });
    replacePermissionsMock.mockReset().mockResolvedValue({
      ...customRole,
      permissionCodes: ['identity.users.read'],
      version: 4
    });
  });

  it('无 identity.roles.assign_permissions 时不显示权限管理按钮', async () => {
    const wrapper = mountWithPermissions(['identity.roles.read']);
    await flushPromises();

    expect(wrapper.text()).not.toContain('权限');
    expect(treeMock).not.toHaveBeenCalled();
  });

  it('打开权限对话框时渲染授权树页面节点', async () => {
    const wrapper = mountWithPermissions(['identity.roles.assign_permissions']);
    await flushPromises();

    await wrapper.get('[data-testid="role-open-permissions"]').trigger('click');
    await flushPromises();

    expect(treeMock).toHaveBeenCalled();
    expect(wrapper.get('[data-testid="role-permission-tree"]').text()).toContain('身份与权限');
    expect(wrapper.get('[data-testid="role-permission-tree"]').text()).toContain('用户管理');
    expect(wrapper.get('[data-testid="role-permission-tree"]').text()).toContain('创建用户');
  });

  it('存在未知已存权限时阻止保存', async () => {
    listMock.mockResolvedValueOnce({
      items: [{
        ...customRole,
        permissionCodes: ['identity.users.read', 'legacy.unknown.permission']
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
    const wrapper = mountWithPermissions(['identity.roles.assign_permissions']);
    await flushPromises();

    await wrapper.get('[data-testid="role-open-permissions"]').trigger('click');
    await flushPromises();

    expect(wrapper.find('[data-testid="role-unknown-permissions"]').exists()).toBe(true);
    expect(wrapper.get('[data-testid="role-save-permissions"]').attributes('disabled')).toBeDefined();
    await wrapper.get('[data-testid="role-save-permissions"]').trigger('click');
    expect(replacePermissionsMock).not.toHaveBeenCalled();
  });

  it('保存时提交排序后的精确权限集合', async () => {
    listMock.mockResolvedValueOnce({
      items: [{
        ...customRole,
        permissionCodes: ['identity.users.create', 'identity.users.read']
      }],
      page: 1,
      pageSize: 20,
      total: 1
    });
    const wrapper = mountWithPermissions(['identity.roles.assign_permissions']);
    await flushPromises();

    await wrapper.get('[data-testid="role-open-permissions"]').trigger('click');
    await flushPromises();
    await wrapper.get('[data-testid="role-save-permissions"]').trigger('click');
    await flushPromises();

    expect(replacePermissionsMock).toHaveBeenCalledWith(
      'role-id',
      ['identity.users.create', 'identity.users.read'],
      3
    );
  });

  it('权限对话框打开后撤权会移除保存按钮', async () => {
    const wrapper = mountWithPermissions(['identity.roles.assign_permissions']);
    await flushPromises();

    await wrapper.get('[data-testid="role-open-permissions"]').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="role-save-permissions"]').exists()).toBe(true);

    const session = useSessionStore();
    session.currentUser = {
      ...session.currentUser!,
      permissions: []
    };
    await nextTick();

    expect(wrapper.find('[data-testid="role-save-permissions"]').exists()).toBe(false);
  });

  it('数据范围对话框打开后撤权会移除保存按钮', async () => {
    const wrapper = mountWithPermissions(['identity.roles.assign_data_scope']);
    await flushPromises();

    await wrapper.get('[data-testid="roles-action-data-scope"]').trigger('click');
    await flushPromises();
    expect(wrapper.find('[data-testid="roles-save-data-scope"]').exists()).toBe(true);

    const session = useSessionStore();
    session.currentUser = {
      ...session.currentUser!,
      permissions: []
    };
    await nextTick();

    expect(wrapper.find('[data-testid="roles-save-data-scope"]').exists()).toBe(false);
  });
});
