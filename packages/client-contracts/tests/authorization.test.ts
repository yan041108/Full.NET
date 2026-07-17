import { describe, expect, it } from 'vitest';
import { isNavigationTree } from '../src/authorization';

const overview = {
  id: 'overview',
  parentId: null,
  routeName: 'overview',
  path: '/',
  componentKey: 'overview',
  title: '工作台',
  caption: '平台运行概览',
  icon: 'dashboard',
  order: 10,
  requiredPermission: 'platform.dashboard.read',
  children: []
};

describe('权限导航契约', () => {
  it('接受完整且父子关系一致的导航树', () => {
    const tree = [{
      ...overview,
      children: [{
        ...overview,
        id: 'tenant-context',
        parentId: 'overview',
        routeName: 'tenant-context',
        path: '/tenant-context',
        componentKey: 'tenant-context',
        title: '租户上下文',
        requiredPermission: 'tenancy.tenants.read'
      }]
    }];

    expect(isNavigationTree(tree)).toBe(true);
  });

  it('拒绝缺字段、重复标识和不一致父节点', () => {
    expect(isNavigationTree([{ id: 'overview', children: [] }])).toBe(false);
    expect(isNavigationTree([overview, { ...overview }])).toBe(false);
    expect(isNavigationTree([{
      ...overview,
      children: [{ ...overview, id: 'child', parentId: 'other' }]
    }])).toBe(false);
  });

  it('拒绝不安全的组件键和异常子节点形状', () => {
    expect(isNavigationTree([{
      ...overview,
      componentKey: '../remote-module'
    }])).toBe(false);
    expect(isNavigationTree([{
      ...overview,
      children: { id: 'child' }
    }])).toBe(false);
  });

  it('验证过程不修改不可信响应', () => {
    const malformed = [{
      ...overview,
      children: [{ ...overview, id: 'child', parentId: 'wrong' }]
    }];
    const before = structuredClone(malformed);

    expect(isNavigationTree(malformed)).toBe(false);
    expect(malformed).toEqual(before);
  });
});
