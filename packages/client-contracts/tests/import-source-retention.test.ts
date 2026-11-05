import { expect, it } from 'vitest';
import { isImportExportSourceRetention } from '../src/import-export.js';

const retained = { expiresAtUtc: '2026-11-12T00:00:00Z', stateKey: 'available', releasedAtUtc: null };

it.each(['available', 'retained_for_execution', 'expired', 'releasing', 'released'])('接受来源保留状态 %s', stateKey => {
  expect(isImportExportSourceRetention({ ...retained, stateKey, releasedAtUtc: stateKey === 'released' ? '2026-11-12T00:01:00Z' : null })).toBe(true);
});

it.each([
  { ...retained, stateKey: 'unknown' }, { ...retained, stateKey: 1 },
  { ...retained, expiresAtUtc: 'not-a-date' }, { ...retained, expiresAtUtc: undefined },
  { ...retained, stateKey: 'released' }, { ...retained, releasedAtUtc: '2026-11-12T00:01:00Z' },
])('拒绝失真的保留状态 %#', value => expect(isImportExportSourceRetention(value)).toBe(false));
