import { existsSync, readFileSync, unlinkSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repoRoot = path.resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const statePath = path.join(repoRoot, 'tests/e2e/admin-real-stack/.stack-state.json');

export default async function globalTeardown() {
  if (process.env.FULLNET_E2E_SKIP_BOOTSTRAP === '1'
    || process.env.FULLNET_E2E_KEEP_STACK === '1') {
    return;
  }

  const { teardownStack } = await import('./scripts/bootstrap-stack.mjs');
  await teardownStack();

  if (existsSync(statePath)) {
    unlinkSync(statePath);
  }
}
