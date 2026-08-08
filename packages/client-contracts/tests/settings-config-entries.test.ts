import { describe, expect, it } from 'vitest';
import {
  isBatchDeleteSettingsConfigEntriesRequest,
  isBatchUpdateConfigValuesRequest,
  isConfigValueUpdate,
  isCreateSettingsConfigEntryRequest,
  isDeleteSettingsConfigEntryRequest,
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
    groupName: '基础',
    valueKind: 'string',
    value: 'Full.NET',
    displayOrder: 10,
    isActive: true,
    createdAtUtc: '2026-01-01T00:00:00+00:00',
    updatedAtUtc: null,
    version: 1
  };

  it('校验配置项详情、分页与写请求', () => {
    expect(isSettingsConfigEntry(configEntry)).toBe(true);
    expect(isSettingsConfigEntry({ ...configEntry, groupName: null })).toBe(true);
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
      groupName: '界面',
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
      groupName: '界面',
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

  it('校验配置项删除与批量请求', () => {
    expect(isDeleteSettingsConfigEntryRequest({ version: 3 })).toBe(true);
    expect(isDeleteSettingsConfigEntryRequest({ version: 1.5 })).toBe(false);
    expect(isDeleteSettingsConfigEntryRequest({})).toBe(false);
    expect(isBatchDeleteSettingsConfigEntriesRequest({
      ids: [configEntryId]
    })).toBe(true);
    expect(isBatchDeleteSettingsConfigEntriesRequest({ ids: [] })).toBe(true);
    expect(isBatchDeleteSettingsConfigEntriesRequest({ ids: [1] })).toBe(false);
    expect(isConfigValueUpdate({
      configKey: 'system.title',
      value: 'Admin'
    })).toBe(true);
    expect(isConfigValueUpdate({
      configKey: 'AB',
      value: 'Admin'
    })).toBe(false);
    expect(isBatchUpdateConfigValuesRequest({
      updates: [{ configKey: 'system.title', value: 'Admin' }]
    })).toBe(true);
    expect(isBatchUpdateConfigValuesRequest({
      updates: [{ configKey: 'AB', value: 'Admin' }]
    })).toBe(false);
  });
});
