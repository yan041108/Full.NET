import test from 'node:test';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { shouldSkipRealStack } from './support/created-app-real-stack.mjs';
import { verifyCreatedEnterpriseApproval } from './support/created-enterprise-approval.mjs';
import { concentratedAcceptanceTimeout, runConcentratedAcceptance } from '../../scripts/testing/test-run-context.mjs';

for (const provider of ['sqlserver', 'mysql']) {
  const skip = shouldSkipRealStack() ? 'enable FULLNET_RUN_TEMPLATE_REAL_STACK=1 for local container acceptance'
    : !areBundleInputsClean() ? 'source bundle inputs have uncommitted changes' : false;
  if ((process.env.CI === 'true' || process.env.CI === '1') && skip)
    throw new Error('Required enterprise approval acceptance cannot skip: ' + skip);
  test(`created enterprise application attachments, approval, reassignment and Worker restart (${provider})`,
    { skip, timeout: concentratedAcceptanceTimeout }, async context => {
      await runConcentratedAcceptance(context, signal => verifyCreatedEnterpriseApproval(provider, { signal }));
    });
}
