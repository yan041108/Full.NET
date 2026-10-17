import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import * as api from './reporting-definitions';
import { reportingDefinition as definition, reportingGroup as group, reportingPort as port, reportingVersion as version } from '../test/reporting-definition-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: {}, request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf294';
const body = () => ({ groupId: definition.groupId, dataSourceId: definition.dataSourceId, name: definition.name,
  description: definition.description, queryPortKey: definition.queryPortKey, parameterSchema: definition.parameterSchema.map(p => ({ ...p })), layoutConfigJson: '{}', isEnabled: true, version: 1 });
beforeEach(() => vi.resetAllMocks());
describe('报表管理响应归属与冻结', () => {
  it.each([{ rows: [group, group] }, { rows: [{ ...group, version: 0 }] }, { rows: [{ ...group, id: '00000000-0000-0000-0000-000000000000' }] }])('拒绝重复、空身份或无效分组修订 %j', async ({ rows }) => {
    vi.mocked(request).mockResolvedValue(rows); await expect(api.listReportingGroups()).rejects.toThrow();
  });
  it.each([{ rows: [definition, definition] }, { rows: [{ ...definition, version: 0 }] }, { rows: [{ ...definition, latestPublishedVersionNumber: -1 }] }])('拒绝无效定义目录 %j', async ({ rows }) => {
    vi.mocked(request).mockResolvedValue(rows); await expect(api.listReportingDefinitions()).rejects.toThrow();
  });
  it('定义查询冻结分组过滤，不因调用方改写而接纳其他分组', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const query = { groupId: definition.groupId };
    const action = api.listReportingDefinitions(query); query.groupId = other; pending.resolve([definition]); await expect(action).resolves.toEqual([definition]);
    vi.mocked(request).mockResolvedValue([definition]); await expect(api.listReportingDefinitions({ groupId: other })).rejects.toThrow();
  });
  it.each([{ id: other }, { version: 1 }, { groupId: other }, { dataSourceId: other }, { parameterSchema: [] }, { layoutConfigJson: '{"changed":true}' }])('更新拒绝错身份、旧修订及配置错配 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...definition, version: 2, ...change }); await expect(api.updateReportingDefinition(definition.id, body())).rejects.toThrow();
  });
  it('定义提交复制嵌套参数，JSON 布局按语义而非空白核对', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const input = body(), action = api.updateReportingDefinition(definition.id, input);
    input.parameterSchema[0]!.defaultValue = '2027'; pending.resolve({ ...definition, version: 2, layoutConfigJson: '{ }' });
    await expect(action).resolves.toMatchObject({ version: 2 }); expect(JSON.parse(vi.mocked(request).mock.calls[0]![1]!.body as string).parameterSchema).toEqual(definition.parameterSchema);
  });
  it('分组更新核对父组与版本，创建核对回显', async () => {
    vi.mocked(request).mockResolvedValue({ ...group, version: 2 }); await expect(api.updateReportingGroup(group.id, { ...group, parentId: other })).rejects.toThrow();
    vi.mocked(request).mockResolvedValue({ ...group, name: '错误' }); await expect(api.createReportingGroup(group)).rejects.toThrow();
  });
  it.each([{ definitionId: other }, { versionNumber: 0 }, { changeNote: '其他备注' }])('发布拒绝错误目标、版本与备注 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...version, ...change }); await expect(api.publishReportingDefinition(definition.id, { version: 1 })).rejects.toThrow();
  });
  it.each([{ rows: [port, port] }, { rows: [{ ...port, queryPortKey: '' }] }, { rows: [{ ...port, parameters: [port.parameters[0], port.parameters[0]] }] }])('拒绝歧义查询端口目录 %j', async ({ rows }) => {
    vi.mocked(request).mockResolvedValue(rows); await expect(api.listReportingQueryPorts()).rejects.toThrow();
  });
  it.each(['group', 'definition'] as const)('%s 删除明确 false 与畸形响应分开处理', async resource => {
    const remove = resource === 'group' ? api.deleteReportingGroup : api.deleteReportingDefinition;
    vi.mocked(request).mockResolvedValueOnce(false).mockResolvedValueOnce({ deleted: true }); await expect(remove(definition.id)).resolves.toBe(false); await expect(remove(definition.id)).rejects.toThrow();
  });
  it('版本列表拒绝空版本身份及重复发布编号', async () => {
    vi.mocked(request).mockResolvedValueOnce([{ ...version, id: '00000000-0000-0000-0000-000000000000' }]).mockResolvedValueOnce([version, { ...version, id: other }]);
    await expect(api.listReportingDefinitionVersions(definition.id)).rejects.toThrow(); await expect(api.listReportingDefinitionVersions(definition.id)).rejects.toThrow();
  });
  it('接受完整健康目录，避免畸形数据场景仅命中基础列表形状守卫', async () => {
    vi.mocked(request).mockResolvedValueOnce([group]).mockResolvedValueOnce([definition]).mockResolvedValueOnce([port]);
    await expect(api.listReportingGroups()).resolves.toEqual([group]); await expect(api.listReportingDefinitions()).resolves.toEqual([definition]); await expect(api.listReportingQueryPorts()).resolves.toEqual([port]);
  });
});
