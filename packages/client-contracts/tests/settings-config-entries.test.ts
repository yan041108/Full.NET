import { describe, expect, it } from 'vitest';
import {
  isCreateSettingsConfigEntryRequest,
  isSettingsConfigEntry,
  isSettingsConfigEntryPage,
  isUpdateSettingsConfigEntryRequest
} from '../src/settings-config-entries';

const configEntryId = '019bc2b1-2a40-7cc3-8992-a80de51bf298';

describe('Settings 系统配置契约', () => {
  const configEntry = {
    id: configEntryId,
    configKey: 'system.title',
    displayName: '系统标题',
    description: '管理端标题',
    valueKind: 'string',
    value: 'Full.NET',
    displayOrder: 10,
    isActive: true,
    version: 1
  };

  it('校验配置项详情、分页与写请求', () => {
    expect(isSettingsConfigEntry(configEntry)).toBe(true);
    expect(isSettingsConfigEntryPage({
      items: [configEntry],
      page: 1,
      pageSize: 20,
      total: 1
    })).toBe(true);
    expect(isSettingsConfigEntry({ ...configEntry, id: 'bad' })).toBe(false);
    expect(isSettingsConfigEntry({ ...configEntry, configKey: 'AB' })).toBe(false);
    expect(isSettingsConfigEntry({ ...configEntry, valueKind: 'xml' })).toBe(false);
    expect(isCreateSettingsConfigEntryRequest({
      configKey: 'ui.theme.mode',
      displayName: '主题模式',
      description: null,
      valueKind: 'string',
      value: 'dark',
      displayOrder: 20
    })).toBe(true);
    expect(isCreateSettingsConfigEntryRequest({
      configKey: 'ui.theme.mode',
      displayName: '主题模式',
      valueKind: 'xml',
      value: 'dark',
      displayOrder: 20
    })).toBe(false);
    expect(isUpdateSettingsConfigEntryRequest({
      displayName: '新名称',
      description: '说明',
      value: 'light',
      displayOrder: 30,
      version: 2
    })).toBe(true);
    expect(isUpdateSettingsConfigEntryRequest({
      displayName: '',
      value: 'light',
      displayOrder: 30,
      version: 2
    })).toBe(false);
  });
});
