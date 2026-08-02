import { describe, expect, it } from 'vitest';
import {
  isAuthorizationTreeModuleArray,
  isAuthorizationTreePageArray
} from '../src/authorization-tree';

const usersPage = {
  id: 'users',
  title: '用户管理',
  permissionCode: 'identity.users.read',
  order: 10,
  actions: [
    {
      id: 'identity.users.reset-password',
      name: '重置密码',
      permissionCode: 'identity.users.reset_password',
      order: 50
    }
  ],
  children: []
};

const identityModule = {
  id: 'identity',
  title: '身份与权限',
  order: 10,
  pages: [usersPage]
};

describe('Host 授权树模块契约', () => {
  it('接受完整模块/页面/操作结构', () => {
    expect(isAuthorizationTreeModuleArray([identityModule])).toBe(true);
    expect(isAuthorizationTreeModuleArray([
      {
        ...identityModule,
        pages: [{
          ...usersPage,
          children: [{
            id: 'roles',
            title: '角色管理',
            permissionCode: 'identity.roles.read',
            order: 20,
            actions: [],
            children: []
          }]
        }]
      }
    ])).toBe(true);
  });

  it('拒绝非数组、缺字段与重复模块标识', () => {
    expect(isAuthorizationTreeModuleArray(null)).toBe(false);
    expect(isAuthorizationTreeModuleArray({})).toBe(false);
    expect(isAuthorizationTreeModuleArray([{ ...identityModule, id: '' }])).toBe(false);
    expect(isAuthorizationTreeModuleArray([identityModule, identityModule])).toBe(false);
    expect(isAuthorizationTreeModuleArray([{ ...identityModule, pages: 'users' }])).toBe(false);
  });

  it('拒绝可执行元数据与未知字段', () => {
    expect(isAuthorizationTreeModuleArray([{
      ...identityModule,
      componentKey: 'identity'
    }])).toBe(false);
    expect(isAuthorizationTreeModuleArray([{
      ...identityModule,
      extra: true
    }])).toBe(false);
  });
});

describe('Host 授权树页面契约（兼容）', () => {
  it('接受完整页面与操作结构', () => {
    expect(isAuthorizationTreePageArray([usersPage])).toBe(true);
    expect(isAuthorizationTreePageArray([
      {
        ...usersPage,
        children: [{
          id: 'roles',
          title: '角色管理',
          permissionCode: 'identity.roles.read',
          order: 20,
          actions: [],
          children: []
        }]
      }
    ])).toBe(true);
  });

  it('拒绝非数组、缺字段与空标识', () => {
    expect(isAuthorizationTreePageArray(null)).toBe(false);
    expect(isAuthorizationTreePageArray({})).toBe(false);
    expect(isAuthorizationTreePageArray([{ ...usersPage, id: '' }])).toBe(false);
    expect(isAuthorizationTreePageArray([{ ...usersPage, title: '   ' }])).toBe(false);
    expect(isAuthorizationTreePageArray([{ id: 'users', children: [] }])).toBe(false);
  });

  it('拒绝重复页面标识与重复操作标识', () => {
    expect(isAuthorizationTreePageArray([usersPage, usersPage])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      actions: [
        usersPage.actions[0],
        usersPage.actions[0]
      ]
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([
      usersPage,
      {
        id: 'roles',
        title: '角色管理',
        permissionCode: 'identity.roles.read',
        order: 20,
        actions: [usersPage.actions[0]],
        children: []
      }
    ])).toBe(false);
  });

  it('拒绝页面读取权限伪装成操作以及重复操作权限', () => {
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      actions: [{
        ...usersPage.actions[0],
        permissionCode: usersPage.permissionCode
      }]
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      actions: [
        usersPage.actions[0],
        {
          ...usersPage.actions[0],
          id: 'identity.users.force-signout',
          permissionCode: usersPage.actions[0].permissionCode
        }
      ]
    }])).toBe(false);
  });

  it('拒绝可执行元数据、未知字段与异常嵌套', () => {
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      componentKey: 'users'
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      actions: [{
        ...usersPage.actions[0],
        path: '/identity/users'
      }]
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      children: { id: 'roles' }
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      actions: 'create'
    }])).toBe(false);
    expect(isAuthorizationTreePageArray([{
      ...usersPage,
      extra: true
    }])).toBe(false);
  });

  it('验证过程不修改不可信响应', () => {
    const malformed = [{
      ...usersPage,
      children: [{ ...usersPage, id: 'roles', parentId: 'wrong' }]
    }];
    const before = structuredClone(malformed);

    expect(isAuthorizationTreePageArray(malformed)).toBe(false);
    expect(malformed).toEqual(before);
  });
});
