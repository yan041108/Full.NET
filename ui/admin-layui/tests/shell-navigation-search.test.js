import { describe, expect, it } from 'vitest';
import { filterShellNavigation } from '../js/core/shell-navigation-search.js';

const navigation = [
  {
    path: '/',
    componentKey: 'overview',
    title: '工作台',
    caption: '平台运行概览'
  },
  {
    path: '/tenants',
    componentKey: 'tenants',
    title: '租户管理',
    caption: 'Host 作用域租户目录'
  }
];

describe('shell-navigation-search', () => {
  it('空查询返回完整授权导航', () => {
    expect(filterShellNavigation(navigation, '')).toHaveLength(2);
  });

  it('按标题过滤导航项', () => {
    expect(filterShellNavigation(navigation, '租户').map(item => item.path)).toEqual(['/tenants']);
  });
});
