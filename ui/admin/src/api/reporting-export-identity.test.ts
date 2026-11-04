import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { createReportingExportTask, listReportingExportTasks } from './reporting-export-tasks';
import { exportTask, deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ http: {}, request: vi.fn(), requestBlob: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
const body = () => ({ definitionId: exportTask.definitionId, versionNumber: 1, formatKey: 'excel', parameters: [{ parameterKey: 'year', value: '2026' }] });
beforeEach(() => vi.resetAllMocks());
describe('报表导出查询与创建归属', () => {
  it.each([
    { items: [exportTask], page: 2, pageSize: 20, total: 1 }, { items: [exportTask], page: 1, pageSize: 10, total: 1 },
    { items: [exportTask, { ...exportTask, id: exportTask.id.toUpperCase() }], page: 1, pageSize: 20, total: 2 },
    { items: [{ ...exportTask, id: '00000000-0000-0000-0000-000000000000' }], page: 1, pageSize: 20, total: 1 }, { items: [], page: 1, pageSize: 20, total: -1 }
  ])('拒绝错误页、重复、空身份或负总数 %j', async value => {
    vi.mocked(request).mockResolvedValue(value); await expect(listReportingExportTasks()).rejects.toThrow(/^client.invalid_reporting_export_task_page$/u);
  });
  it('指定定义过滤接受UUID大小写等价，拒绝错定义', async () => {
    vi.mocked(request).mockResolvedValue({ items: [exportTask], page: 1, pageSize: 20, total: 1 }); await expect(listReportingExportTasks(1, 20, exportTask.definitionId.toUpperCase())).resolves.toMatchObject({ items: [exportTask] }); await expect(listReportingExportTasks(1, 20, other)).rejects.toThrow(/^client.invalid_reporting_export_task_page$/u);
  });
  it.each([{ definitionId: other }, { versionNumber: 2 }, { formatKey: 'pdf' }, { parameters: [{ parameterKey: 'year', value: '2025' }] }, { parameters: [] }])('创建拒绝错配定义、版本、格式或参数 %j', async change => {
    vi.mocked(request).mockResolvedValue({ ...exportTask, parameters: body().parameters, ...change }); await expect(createReportingExportTask(body())).rejects.toThrow(/^client.invalid_reporting_export_task_identity$/u);
  });
  it('创建发送和响应核对共用参数副本，不借用调用方的在途修改', async () => {
    const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const payload = body(), creating = createReportingExportTask(payload); payload.definitionId = other; payload.parameters[0]!.value = 'changed'; pending.resolve({ ...exportTask, parameters: body().parameters }); await expect(creating).resolves.toMatchObject({ definitionId: exportTask.definitionId }); expect(JSON.parse(vi.mocked(request).mock.calls[0]![1]!.body as string)).toEqual(body());
  });
  it('省略版本仍允许服务端解析发布版本，参数顺序可等价', async () => {
    const payload = { ...body(), versionNumber: undefined, parameters: [{ parameterKey: 'a', value: null }, { parameterKey: 'b', value: 'x' }] }; vi.mocked(request).mockResolvedValue({ ...exportTask, parameters: [...payload.parameters].reverse() }); await expect(createReportingExportTask(payload)).resolves.toMatchObject({ versionNumber: 1 });
  });
  it.each([{ parameters: [{ parameterKey: '', value: null }] }, { parameters: [{ parameterKey: 'x', value: 'a' }, { parameterKey: 'x', value: 'b' }] }])('空键或重复参数在请求前拒绝 %j', async change => {
    await expect(createReportingExportTask({ ...body(), ...change })).rejects.toThrow(/^client.invalid_create_reporting_export_task$/u); expect(request).not.toHaveBeenCalled();
  });
});
