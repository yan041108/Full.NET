import test from 'node:test';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { verifyCreatedAppRealStack } from './support/created-app-real-stack.mjs';

function realStackSkipReason() {
  if (process.env.FULLNET_SKIP_TESTCONTAINERS === '1') {
    return 'FULLNET_SKIP_TESTCONTAINERS=1';
  }
  if (process.env.FULLNET_RUN_TEMPLATE_REAL_STACK !== '1' && process.env.CI !== 'true' && process.env.CI !== '1') {
    return 'local run requires FULLNET_RUN_TEMPLATE_REAL_STACK=1 (CI runs automatically)';
  }
  if (!areBundleInputsClean()) {
    return 'source bundle inputs have uncommitted changes';
  }
  return false;
}

for (const database of ['sqlserver', 'mysql']) {
  test(`created minimal application real-stack (${database})`, { timeout: 900_000, skip: realStackSkipReason() }, async () => {
    await verifyCreatedAppRealStack(database);
  });
}
