import { beforeEach, describe, expect, it, vi } from 'vitest';
import { request } from './http';
import { listGoViewProjects, getGoViewProject, createGoViewProject, updateGoViewProject, publishGoViewProject, previewGoViewProject } from './goview-projects';
import { goViewProject as row, goViewVersion, goViewPreview } from '../test/goview-fixtures';
import { deferred } from '../test/data-output-fixtures';
vi.mock('./http', () => ({ request: vi.fn() }));
const other = '019bc2b1-2a40-7cc3-8992-a80de51bf298';
beforeEach(() => vi.resetAllMocks());
describe('大屏项目读写与版本身份', () => {
  it.each([{ rows: [row, { ...row, id: row.id.toUpperCase() }] }, { rows: [{ ...row, version: 0 }] }, { rows: [{ ...row, latestPublishedVersionNumber: 0.5 }] }])('拒绝重复或非法修订目录 %j', async ({ rows }) => { vi.mocked(request).mockResolvedValue(rows); await expect(listGoViewProjects()).rejects.toThrow(); });
  it('详情不能返回另一个项目', async () => { vi.mocked(request).mockResolvedValue({ ...row, id: other }); await expect(getGoViewProject(row.id)).rejects.toThrow(); });
  it.each([{ version: 2 }, { latestPublishedVersionNumber: 1 }, { projectKey: 'other' }])('创建拒绝非初始草稿或错误稳定键 %j', async change => { vi.mocked(request).mockResolvedValue({ ...row, ...change }); await expect(createGoViewProject({ projectKey: row.projectKey, name: row.name, isEnabled: true })).rejects.toThrow(); });
  it('创建接受服务端默认空白画布', async () => { vi.mocked(request).mockResolvedValue(row); await expect(createGoViewProject({ projectKey: row.projectKey, name: row.name, isEnabled: true })).resolves.toEqual(row); });
  it.each([{ id: other }, { version: 1 }, { name: 'other' }, { canvasJson: '{}' }])('更新拒绝错误资源、旧修订和错配配置 %j', async change => { vi.mocked(request).mockResolvedValue({ ...row, version: 2, ...change }); await expect(updateGoViewProject(row.id, { name: row.name, canvasJson: row.canvasJson, isEnabled: true, version: 1 })).rejects.toThrow(); });
  it('更新冻结已发送输入，调用方后改不影响回包归属', async () => { const pending = deferred<unknown>(); vi.mocked(request).mockReturnValueOnce(pending.promise); const body = { name: row.name, canvasJson: row.canvasJson, isEnabled: true, version: 1 }, action = updateGoViewProject(row.id, body); body.name = 'later'; pending.resolve({ ...row, version: 2 }); await expect(action).resolves.toMatchObject({ name: row.name }); });
  it.each([{ projectId: other }, { id: '00000000-0000-0000-0000-000000000000' }, { versionNumber: 0 }, { changeNote: 'other' }])('发布校验真实版本响应 %j', async change => { vi.mocked(request).mockResolvedValue({ ...goViewVersion(), ...change }); await expect(publishGoViewProject(row.id, { version: 1 })).rejects.toThrow(); });
  it('发布返回版本 DTO，不丢弃服务端回执', async () => { vi.mocked(request).mockResolvedValue(goViewVersion()); await expect(publishGoViewProject(row.id, { version: 1 })).resolves.toEqual(goViewVersion()); });
  it.each([{ projectId: other }, { versionNumber: 2 }])('精确预览拒绝错配身份或版本 %j', async change => { vi.mocked(request).mockResolvedValue({ ...goViewPreview(), ...change }); await expect(previewGoViewProject(row.id, { versionNumber: 1 })).rejects.toThrow(); });
});
