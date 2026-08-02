import { describe, expect, it } from 'vitest';
import { isAuthorizationTreePageArray } from '../src/authorization-tree';

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

describe('Host 授权树客户端契约', () => {
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