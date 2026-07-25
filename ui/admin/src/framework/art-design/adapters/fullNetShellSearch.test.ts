import { describe, expect, it } from 'vitest';
import { Grid } from '@element-plus/icons-vue';
import type { ShellNavigationItem } from './fullNetShellAdapter';
import { filterShellNavigation } from './fullNetShellSearch';

const navigation: ShellNavigationItem[] = [{
  path: '/',
  routeName: 'overview',
  componentKey: 'overview',
  title: '工作台',
  caption: '平台运行概览',
  icon: Grid
}, {
  path: '/tenants',
  routeName: 'tenant-management',
  componentKey: 'tenants',
  title: '租户管理',
  caption: 'Host 作用域租户目录',
  icon: Grid
}];

describe('fullNetShellSearch', () => {
  it('空查询返回完整授权导航', () => {
    expect(filterShellNavigation(navigation, '')).toHaveLength(2);
  });

  it('按标题过滤并拒绝未知路径', () => {
    expect(filterShellNavigation(navigation, '租户').map(item => item.path))
      .toEqual(['/tenants']);
  });
});
