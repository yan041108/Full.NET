import { describe, expect, it } from 'vitest';
import {
  isSettingsEnumCatalogDetail,
  isSettingsEnumCatalogSummary
} from '../src/settings-enum-catalogs';
import { createAdminNavigationCatalog } from '../src/navigation-catalog';

describe('Settings 枚举目录契约', () => {
  it('校验目录摘要与详情', () => {
    expect(isSettingsEnumCatalogSummary({
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: null,
      memberCount: 5
    })).toBe(true);
    expect(isSettingsEnumCatalogSummary({
      key: 'Bad',
      displayName: 'x',
      description: null,
      memberCount: 1
    })).toBe(false);
    expect(isSettingsEnumCatalogDetail({
      key: 'settings.config_value_kind',
      displayName: '配置值类型',
      description: '说明',
      members: [
        { code: 'string', label: 'string', displayOrder: 0 }
      ]
    })).toBe(true);
  });

  it('导航白名单发布 enum-catalogs', () => {
    const catalog = createAdminNavigationCatalog();
    expect(catalog.localNavigationFor('enum-catalogs')).toEqual({
      componentKey: 'enum-catalogs',
      routeName: 'enum-catalogs',
      path: '/settings/enum-catalogs'
    });
  });
});
