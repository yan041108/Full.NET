import test from 'node:test';
import { areBundleInputsClean } from '../../scripts/templates/build-source-bundle.mjs';
import { shouldSkipRealStack } from './support/created-app-real-stack.mjs';
import { verifyCreatedEnterpriseDataDelivery } from './support/created-enterprise-data-delivery.mjs';

for (const provider of ['sqlserver','mysql']) {
 const skip = shouldSkipRealStack() ? 'enable FULLNET_RUN_TEMPLATE_REAL_STACK=1 for local container acceptance'
  : !areBundleInputsClean() ? 'source bundle inputs have uncommitted changes' : false;
 if ((process.env.CI === 'true' || process.env.CI === '1') && skip) throw new Error('Required enterprise application acceptance cannot skip: '+skip);
 test(`created enterprise application workbook API and independent Worker (${provider})`,{skip,timeout:900_000}, async context => {
  await verifyCreatedEnterpriseDataDelivery(provider,{signal:context.signal});
 });
}
