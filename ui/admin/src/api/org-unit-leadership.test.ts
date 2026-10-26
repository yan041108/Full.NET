import { beforeEach, describe, expect, it, vi } from 'vitest';
import { http } from './http';
import { getOrganizationUnitLeadership, listOrganizationUnitLeaderCandidates, setOrganizationUnitLeadership } from './org-unit-leadership';

vi.mock('./http', () => ({ http: { request: vi.fn() } }));
const unit = '01912345-6789-7abc-8def-0123456789ab'; const actor = '01912345-6789-7abc-8def-0123456789ac';
const leader = '01912345-6789-7abc-8def-0123456789ad';
const value = { unitId: unit, unitVersion: 3, isUnitActive: true, leaderUserId: leader, revision: 1,
  updatedById: actor, updatedAtUtc: '2026-10-25T00:00:00Z', leaderDisplayName: 'Leader' };
beforeEach(() => vi.clearAllMocks());
describe('机构领导权传输', () => {
  it('读取绑定准确机构，规范化UUID及安全整数，不接受其他资源的回包', async () => {
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, revision: '1' });
    expect(await getOrganizationUnitLeadership(unit.toUpperCase())).toEqual(value);
    expect(http.request).toHaveBeenCalledWith(`/api/v1/organization/units/${unit}/leadership`, { method: 'GET' }, undefined);
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, unitId: actor }); await expect(getOrganizationUnitLeadership(unit)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, revision: '9007199254740993' }); await expect(getOrganizationUnitLeadership(unit)).rejects.toThrow();
  });
  it('只提交负责人和两个乐观版本，保留取消信号，拒绝服务端换人', async () => {
    const signal = new AbortController().signal; vi.mocked(http.request).mockResolvedValueOnce(value);
    expect(await setOrganizationUnitLeadership(unit, { leaderUserId: leader.toUpperCase(), expectedUnitVersion: 3, expectedRevision: 0, tenantId: actor } as never, signal)).toEqual(value);
    const args = vi.mocked(http.request).mock.calls[0]!;
    expect(JSON.parse(args[1]!.body as string)).toEqual({ leaderUserId: leader, expectedUnitVersion: 3, expectedRevision: 0 }); expect(args[2]).toBe(signal);
    vi.mocked(http.request).mockResolvedValueOnce({ ...value, leaderUserId: actor }); await expect(setOrganizationUnitLeadership(unit, { leaderUserId: leader, expectedUnitVersion: 3, expectedRevision: 0 })).rejects.toThrow();
  });
  it('非法身份和版本在HTTP调用前失败', async () => {
    await expect(getOrganizationUnitLeadership('00000000-0000-0000-0000-000000000000')).rejects.toThrow();
    await expect(setOrganizationUnitLeadership(unit, { leaderUserId: leader, expectedUnitVersion: 0, expectedRevision: 0 })).rejects.toThrow();
    await expect(setOrganizationUnitLeadership(unit, { leaderUserId: leader, expectedUnitVersion: 3, expectedRevision: '0' } as never)).rejects.toThrow();
    expect(http.request).not.toHaveBeenCalled();
  });
  it('候选保留真实分页，不接受大小写重复身份或越过总量的伪页', async () => {
    const candidate = { id: leader, username: 'last', displayName: 'Last' };
    vi.mocked(http.request).mockResolvedValueOnce({ items: [candidate], page: 2, pageSize: 20, total: '21' });
    expect(await listOrganizationUnitLeaderCandidates(unit, 2, 20)).toEqual({ items: [candidate], page: 2, pageSize: 20, total: 21 });
    vi.mocked(http.request).mockResolvedValueOnce({ items: [candidate, { ...candidate, id: leader.toUpperCase() }], page: 1, pageSize: 20, total: 2 });
    await expect(listOrganizationUnitLeaderCandidates(unit)).rejects.toThrow();
    vi.mocked(http.request).mockResolvedValueOnce({ items: [candidate], page: 2, pageSize: 20, total: 1 });
    await expect(listOrganizationUnitLeaderCandidates(unit, 2)).rejects.toThrow();
  });
});
