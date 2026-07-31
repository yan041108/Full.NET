import { describe, expect, it } from 'vitest';
import {
  isCreateSettingsDictItemRequest,
  isCreateSettingsDictTypeRequest,
  isSettingsDictItem,
  isSettingsDictItemPage,
  isSettingsDictType,
  isSettingsDictTypePage,
  isUpdateSettingsDictItemRequest,
  isUpdateSettingsDictTypeRequest
} from '../src/settings-dict-types';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

const dictTypeId = '019bc2b1-2a40-7cc3-8992-a80de51bf296';
const dictItemId = '019bc2b1-2a40-7cc3-8992-a80de51bf297';

describe('Settings 数据字典契约', () => {
  const dictType = {
    id: dictTypeId,
    code: 'gender',
    name: '性别',
    description: '通用性别枚举',
    displayOrder: 10,
    isActive: true,
    version: 1
  };

  const dictItem = {
    id: dictItemId,
    dictTypeId,
    label: '男',
    value: 'male',
    color: '#409eff',
    displayOrder: 1,
    isActive: true,
    version: 1
  };

  it('校验字典类型详情、分页与写请求', () => {
    expect(isSettingsDictType(dictType)).toBe(true);
    expect(isSettingsDictTypePage({
      items: [dictType],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isSettingsDictType({ ...dictType, id: 'bad' })).toBe(false);
    expect(isSettingsDictType({ ...dictType, code: 'AB' })).toBe(false);
    expect(isCreateSettingsDictTypeRequest({
      code: 'order_status',
      name: '订单状态',
      description: null,
      displayOrder: 20
    })).toBe(true);
    expect(isCreateSettingsDictTypeRequest({
      code: 'order_status',
      name: '订单状态',
      displayOrder: 1.5
    })).toBe(false);
    expect(isUpdateSettingsDictTypeRequest({
      name: '新名称',
      description: '说明',
      displayOrder: 30,
      version: 2
    })).toBe(true);
    expect(isUpdateSettingsDictTypeRequest({
      name: '',
      displayOrder: 30,
      version: 2
    })).toBe(false);
  });

  it('校验字典项详情、分页与写请求', () => {
    expect(isSettingsDictItem(dictItem)).toBe(true);
    expect(isSettingsDictItemPage({
      items: [dictItem],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isSettingsDictItem({ ...dictItem, value: 'Male' })).toBe(false);
    expect(isCreateSettingsDictItemRequest({
      label: '女',
      value: 'female',
      color: null,
      displayOrder: 2
    })).toBe(true);
    expect(isCreateSettingsDictItemRequest({
      label: '女',
      value: 'F',
      displayOrder: 2
    })).toBe(false);
    expect(isUpdateSettingsDictItemRequest({
      label: '男性',
      color: '#67c23a',
      displayOrder: 1,
      version: 2
    })).toBe(true);
    expect(isUpdateSettingsDictItemRequest({
      label: '',
      displayOrder: 1,
      version: 2
    })).toBe(false);
  });

  it('导航白名单发布 dict-types、tenant-dict-types 与 config-entries 组件键', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('dict-types')).toEqual({
      componentKey: 'dict-types',
      routeName: 'dict-types',
      path: '/settings/dict-types'
    });
    expect(catalog.localNavigationFor('tenant-dict-types')).toEqual({
      componentKey: 'tenant-dict-types',
      routeName: 'tenant-dict-types',
      path: '/settings/tenant-dict-types'
    });
    expect(catalog.localNavigationFor('config-entries')).toEqual({
      componentKey: 'config-entries',
      routeName: 'config-entries',
      path: '/settings/config-entries'
    });
  });
});
