import test from 'node:test';
import { readFile } from 'node:fs/promises';

const foundationFixtures = [
  'public-auth-v1.json',
  'tenancy-entitlements-v1.json',
  'tenant-members-v1.json',
  'tenant-subscriptions-v1.json',
  'identity-mfa-recovery-codes-v1.json',
];

test('基础 API 夹具存在', async () => {
  for (const name of foundationFixtures) {
    const value = JSON.parse(
      await readFile(new URL(`../../contracts/openapi/${name}`, import.meta.url), 'utf8'));
    if (!value.paths || Object.keys(value.paths).length === 0) {
      throw new Error(name);
    }
  }
});
