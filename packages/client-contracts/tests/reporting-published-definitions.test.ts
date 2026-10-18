import { describe, expect, it } from 'vitest';
import { isReportingPublishedDefinitionList } from '../src/reporting-definitions.js';
const item = { definitionId: '019bc2b1-2a40-7cc3-8992-a80de51bf299', definitionKey: 'fixture', name: '报表',
  versionNumber: 1, queryPortKey: 'fixture', parameterSchema: [], layoutConfigJson: '{}' };
describe('租户发布目录契约', () => {
  it('接受明确正整数版本与空目录', () => {
    expect(isReportingPublishedDefinitionList([item])).toBe(true);
    expect(isReportingPublishedDefinitionList([])).toBe(true);
  });
  it.each([0, -1, 1.5, NaN, '1', null])('拒绝无效版本 %s', versionNumber => {
    expect(isReportingPublishedDefinitionList([{ ...item, versionNumber }])).toBe(false);
  });
  it('拒绝草稿对象和损坏参数结构', () => {
    expect(isReportingPublishedDefinitionList([{ ...item, definitionId: undefined, id: item.definitionId }])).toBe(false);
    expect(isReportingPublishedDefinitionList([{ ...item, parameterSchema: [{}] }])).toBe(false);
  });
});
