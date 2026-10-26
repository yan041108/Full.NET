import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getOrganizationReportingLine, listOrganizationReportingCandidates, setOrganizationReportingLine } from './org-reporting-lines';
vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const employee = '01912345-6789-7abc-8def-0123456789ab'; const manager = '01912345-6789-7abc-8def-0123456789ac';
const value = { employeeUserId: employee, managerUserId: manager, revision: 2, updatedById: employee, updatedAtUtc: '2026-10-25T00:00:00Z', managerDisplayName: 'Manager' };
beforeEach(() => vi.clearAllMocks());
describe('直属汇报关系传输', () => {
  it('规范化身份和修订但拒绝错人回包及自指关系', async () => {
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, revision: '2' }); expect(await getOrganizationReportingLine(employee.toUpperCase())).toEqual(value);
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, employeeUserId: manager }); await expect(getOrganizationReportingLine(employee)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, managerUserId: employee }); await expect(getOrganizationReportingLine(employee)).rejects.toThrow();
  });
  it('只写入直属上级和读取修订，清空保持显式空值', async () => {
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, managerUserId: null, managerDisplayName: null }); const signal = new AbortController().signal;
    expect((await setOrganizationReportingLine(employee, null, 1, signal)).managerUserId).toBeNull();
    expect(JSON.parse(vi.mocked(http.request).mock.calls[0]![1]!.body as string)).toEqual({ managerUserId: null, expectedRevision: 1 });
    expect(vi.mocked(http.request).mock.calls[0]![2]).toBe(signal);
  });
  it('自身、空UUID或不安全修订在发送前拒绝', async () => {
    await expect(setOrganizationReportingLine(employee, employee.toUpperCase(), 1)).rejects.toThrow();
    await expect(setOrganizationReportingLine(employee, manager, Number.MAX_SAFE_INTEGER + 1)).rejects.toThrow();
    await expect(getOrganizationReportingLine('00000000-0000-0000-0000-000000000000')).rejects.toThrow(); expect(http.request).not.toHaveBeenCalled();
  });
  it('第二页不丢失候选，大小写重复和越过总量必须拒绝', async () => {
    const person = { id: manager, username: 'manager', displayName: 'Manager' };
    vi.mocked(http.request).mockResolvedValueOnce({ items: [person], page: 2, pageSize: 20, total: 21 }); expect((await listOrganizationReportingCandidates(employee, 2)).items).toEqual([person]);
    vi.mocked(http.request).mockResolvedValueOnce({ items: [person, { ...person, id: manager.toUpperCase() }], page: 1, pageSize: 20, total: 2 }); await expect(listOrganizationReportingCandidates(employee)).rejects.toThrow();
  });
});
