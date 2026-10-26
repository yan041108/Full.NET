import { describe, expect, it } from 'vitest';
import { isWorkflowClaimProgress, readWorkflowClaimPoolPage, readWorkflowClaimRequest, readWorkflowReleaseRequest } from '../src/workflow-claims.js';

describe('领取池契约', () => {
  it('领取只接受固定修订与幂等键，拒绝覆盖用户或租户', () => {
    const request = { expectedRevision: 1, idempotencyKey: 'claim-one' };
    expect(readWorkflowClaimRequest(request)).toEqual(request);
    expect(() => readWorkflowClaimRequest({ ...request, actorUserId: 'foreign' })).toThrow();
    expect(() => readWorkflowClaimRequest({ ...request, tenantId: 'foreign' })).toThrow();
    expect(() => readWorkflowClaimRequest({ ...request, expectedRevision: 0 })).toThrow();
  });
  it('释放独立绑定三个修订，不发送表单补丁', () => {
    const request = { expectedRevision: 2, expectedPoolRevision: 3, expectedFormRevision: 4, idempotencyKey: 'release-one' };
    expect(readWorkflowReleaseRequest(request)).toEqual(request);
    expect(() => readWorkflowReleaseRequest({ ...request, fieldPatch: {} })).toThrow();
    expect(() => readWorkflowReleaseRequest({ ...request, expectedPoolRevision: 0 })).toThrow();
  });
  it('循环预算耗尽不能声明可释放或重新可领取', () => {
    expect(isWorkflowClaimProgress({ poolStatusKey: 'claimed', poolRevision: 2, claimCycle: 1, allowRelease: true })).toBe(true);
    expect(isWorkflowClaimProgress({ poolStatusKey: 'claimed', poolRevision: 33, claimCycle: 32, allowRelease: true })).toBe(false);
    expect(isWorkflowClaimProgress({ poolStatusKey: 'available', poolRevision: 33, claimCycle: 32, allowRelease: false })).toBe(false);
  });
  it('可领取列表拒绝越界和不完整资源', () => {
    expect(readWorkflowClaimPoolPage({ items: [], totalCount: 0, page: 1, pageSize: 20 }).items).toEqual([]);
    expect(() => readWorkflowClaimPoolPage({ items: [{}], totalCount: 1, page: 1, pageSize: 20 })).toThrow();
    expect(() => readWorkflowClaimPoolPage({ items: [], totalCount: 0, page: 1, pageSize: 101 })).toThrow();
  });
});
