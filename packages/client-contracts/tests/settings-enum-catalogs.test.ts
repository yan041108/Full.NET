import { describe, expect, it } from 'vitest';
import {
  isSettingsEnumCatalogDetail,
  isSettingsEnumCatalogDictGenerationPreview,
  isSettingsEnumCatalogDictGenerationResult,
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

  it('校验字典生成预览与结果', () => {
    expect(isSettingsEnumCatalogDictGenerationPreview({
      catalogKey: 'settings.config_value_kind',
      dictTypeCode: 'settings.config_value_kind',
      dictTypeName: '配置值类型',
      dictTypeExists: false,
      willCreateDictType: true,
      items: [
        {
          value: 'string',
          proposedLabel: 'string',
          existingLabel: null,
          displayOrder: 0,
          action: 'create'
        }
      ],
      unmanagedItems: []
    })).toBe(true);
    expect(isSettingsEnumCatalogDictGenerationResult({
      catalogKey: 'settings.config_value_kind',
      dictTypeCode: 'settings.config_value_kind',
      dictTypeId: '018f1234-5678-7abc-8def-0123456789ab',
      dictTypeCreated: true,
      itemsCreated: 1,
      itemsSkipped: 0,
      itemsConflicted: 0,
      itemsInvalid: 0,
      items: []
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
