import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { listWorkflowWorkCalendars, listWorkflowWorkCalendarVersions, getWorkflowWorkCalendar, getWorkflowWorkCalendarVersion,
  createWorkflowWorkCalendar, updateWorkflowWorkCalendar, publishWorkflowWorkCalendar } from './workflow-calendars';
import { defaultWorkflowWorkCalendar } from '../workflow/workflow-work-calendar';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const id = '019a23f0-0000-7000-8000-000000000001'; const versionId = '019a23f0-0000-7000-8000-000000000002'; const foreign = '019a23f0-0000-7000-8000-000000000003';
const summary = { id, calendarKey: 'office.week', displayName: 'Office', revision: 2, latestVersionNumber: 1, latestVersionId: versionId, timeZoneKey: 'UTC', createdAtUtc: '2026-10-26T00:00:00Z', updatedAtUtc: '2026-10-26T00:00:00Z' };
const detail = { ...summary, draft: defaultWorkflowWorkCalendar() };
const version = { id: versionId, calendarId: id, versionNumber: 1, contentHash: 'a'.repeat(64), timeZoneKey: 'UTC', publishedAtUtc: '2026-10-26T00:00:00Z', calendar: defaultWorkflowWorkCalendar() };
beforeEach(() => vi.clearAllMocks());
describe('工作日历API边界', () => {
  it('读取有界分页和原版本，拒绝替换资源', async () => {
    vi.mocked(http.request).mockResolvedValue({ items: [summary], nextAfterId: null });expect((await listWorkflowWorkCalendars()).items).toHaveLength(1);
    expect(http.request).toHaveBeenLastCalledWith('/api/v1/workflow/work-calendars', expect.objectContaining({ method: 'GET' }), undefined);
    vi.mocked(http.request).mockResolvedValue(detail);expect(await getWorkflowWorkCalendar(id)).toEqual(detail);
    vi.mocked(http.request).mockResolvedValue({ ...detail, id: foreign });await expect(getWorkflowWorkCalendar(id)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValue(version);expect(await getWorkflowWorkCalendarVersion(versionId)).toEqual(version);
    vi.mocked(http.request).mockResolvedValue({ ...version, timeZoneKey: 'Asia/Shanghai' });await expect(getWorkflowWorkCalendarVersion(versionId)).rejects.toThrow();
  });
  it('分页拒绝重复项、超界数量、假游标和错误父日历', async () => {
    for (const page of [{ items: [summary, summary], nextAfterId: null }, { items: Array.from({ length: 21 }, () => summary), nextAfterId: null }, { items: [summary], nextAfterId: foreign }]) {
      vi.mocked(http.request).mockResolvedValue(page);await expect(listWorkflowWorkCalendars()).rejects.toThrow();
    }
    vi.mocked(http.request).mockResolvedValue({ items: [{ ...version, calendarId: foreign }], nextBeforeVersionNumber: null });await expect(listWorkflowWorkCalendarVersions(id)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValue({ items: [{ ...version, id: foreign, versionNumber: 2 }, version], nextBeforeVersionNumber: null });expect((await listWorkflowWorkCalendarVersions(id)).items).toHaveLength(2);
    await expect(listWorkflowWorkCalendars('00000000-0000-0000-0000-000000000000')).rejects.toThrow();
  });
  it('创建、保存及发布均使用既定资源和修订', async () => {
    const body = { calendarKey: summary.calendarKey, displayName: summary.displayName, draft: defaultWorkflowWorkCalendar() };
    vi.mocked(http.request).mockResolvedValue({ ...detail, revision: 1 });expect((await createWorkflowWorkCalendar(body)).revision).toBe(1);
    vi.mocked(http.request).mockResolvedValue({ ...detail, revision: 3 });expect((await updateWorkflowWorkCalendar(id, { expectedRevision: 2, displayName: 'Office', draft: body.draft })).revision).toBe(3);
    vi.mocked(http.request).mockResolvedValue(version);expect(await publishWorkflowWorkCalendar(id, { expectedRevision: 2 })).toEqual(version);
    vi.mocked(http.request).mockResolvedValue({ ...version, calendarId: foreign });await expect(publishWorkflowWorkCalendar(id, { expectedRevision: 2 })).rejects.toThrow();
  });
  it('写入禁止夹带租户、身份或发布快照', async () => {
    const body = { calendarKey: summary.calendarKey, displayName: 'Office', draft: defaultWorkflowWorkCalendar() };
    await expect(createWorkflowWorkCalendar({ ...body, tenantId: foreign } as typeof body)).rejects.toThrow();
    await expect(updateWorkflowWorkCalendar(id, { expectedRevision: 2, displayName: 'Office', draft: body.draft, actorUserId: foreign } as Parameters<typeof updateWorkflowWorkCalendar>[1])).rejects.toThrow();
    await expect(publishWorkflowWorkCalendar(id, { expectedRevision: 2, boundCalendar: body.draft } as { expectedRevision: number })).rejects.toThrow();
    expect(http.request).not.toHaveBeenCalled();
  });
});
