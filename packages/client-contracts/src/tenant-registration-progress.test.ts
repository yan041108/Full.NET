import { describe, expect, it } from 'vitest';
import { readTenantRegistrationProgress, readTenantRegistrationProgressList } from './tenant-registration-progress.js';

const row = { id: '01981a3f-00c0-7000-8000-000000000010', tenantId: '01981a3f-00c0-7000-8000-000000000011',
  registrationWayName: null, phase: 'Cancelled', errorCode: 'quota.rejected', createdAtUtc: '2026-11-04T00:00:00Z', version: 3, canContinue: true };
describe('注册入驻进度契约', () => {
  it('保留未完成进度及可空名称', () => { expect(readTenantRegistrationProgress(row)).toEqual(row); });
  it.each([{ phase: 'Success' }, { version: 0 }, { version: 1.5 }, { tenantId: 'foreign' }, { createdAtUtc: 'invalid' }, { canContinue: 'yes' }])('拒绝损坏进度 %j', change => {
    expect(() => readTenantRegistrationProgress({ ...row, ...change })).toThrow();
  });
  it('拒绝重复和超出服务端边界的进度列表', () => {
    expect(() => readTenantRegistrationProgressList([row, row])).toThrow();
    expect(() => readTenantRegistrationProgressList(Array(21).fill(row))).toThrow();
  });
});
