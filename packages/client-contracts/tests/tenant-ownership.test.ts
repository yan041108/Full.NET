import { expect, it } from 'vitest';
import { isTenantOwnershipState, isTenantOwnerTransferReceipt } from '../src/tenant-ownership';
const id = (n: number) => `01981a3f-00c0-7000-8000-${String(n).padStart(12, '0')}`;
const member = { id: id(1), tenantId: id(2), userId: id(3), username: 'owner', displayName: 'Owner', memberRole: 'Owner', status: 'Active',
  version: 4, createdAtUtc: '2026-11-04T00:00:00Z', updatedAtUtc: '2026-11-04T00:00:00Z' };
const receipt = { tenantId: id(2), operationId: id(4), actorUserId: id(3), sourceMemberId: id(1), sourceUserId: id(3), sourcePreviousVersion: 4,
  sourceVersion: 5, targetMemberId: id(5), targetUserId: id(6), targetPreviousVersion: 2, targetVersion: 3, createdAtUtc: '2026-11-04T00:00:00Z' };
it('同时接受已建立职责和无 Owner 的首次建立状态', () => {
  expect(isTenantOwnershipState({ tenantId: id(2), owner: member, canInitialize: false, canTransfer: true, requiresTotp: true })).toBe(true);
  expect(isTenantOwnershipState({ tenantId: id(2), owner: null, canInitialize: true, canTransfer: false, requiresTotp: false })).toBe(true);
});
it.each([{ tenantId: id(9) }, { memberRole: 'Admin' }, { version: 0 }, { status: 'Removed' }])('拒绝跨企业、错误职责或不一致授权状态：%j', patch => {
  expect(isTenantOwnershipState({ tenantId: id(2), owner: { ...member, ...patch }, canInitialize: false, canTransfer: true, requiresTotp: true })).toBe(false);
});
it('回执的源职责必须属于原操作者，源目标不能相同且版本必须精确递增', () => {
  expect(isTenantOwnerTransferReceipt(receipt)).toBe(true);
  expect(isTenantOwnerTransferReceipt({ ...receipt, sourceMemberId: null, sourceUserId: null, sourcePreviousVersion: null, sourceVersion: null })).toBe(true);
});
it.each([{ sourceUserId: id(9) }, { targetUserId: id(3) }, { targetMemberId: id(1) }, { sourceVersion: 7 },
  { targetVersion: 2 }, { targetPreviousVersion: Number.MAX_SAFE_INTEGER, targetVersion: Number.MAX_SAFE_INTEGER + 1 }, { sourceMemberId: null }])('拒绝矛盾回执：%j', patch => {
  expect(isTenantOwnerTransferReceipt({ ...receipt, ...patch })).toBe(false);
});
