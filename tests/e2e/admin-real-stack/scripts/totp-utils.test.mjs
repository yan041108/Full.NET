import assert from 'node:assert/strict';
import test from 'node:test';
import { computeTotpCode, decodeBase32Secret } from './totp-utils.mjs';

test('Base32 解码与 6 位 TOTP 与固定步长对齐', () => {
  const secret = 'JBSWY3DPEHPK3PXP';
  const key = decodeBase32Secret(secret);
  const utcMs = 59 * 1_000_000_000;
  const code = computeTotpCode(key, utcMs);
  assert.match(code, /^\d{6}$/);
  assert.equal(code, computeTotpCode(key, utcMs));
});
