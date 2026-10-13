import test from 'node:test';
import {areBundleInputsClean} from '../../scripts/templates/build-source-bundle.mjs';
import {shouldSkipRealStack} from './support/created-app-real-stack.mjs';
import {prepareCreatedImportRecovery,verifyCreatedImportRecovery} from './support/created-enterprise-import-recovery.mjs';
import {concentratedAcceptanceTimeout,runConcentratedAcceptance,withTestRun} from '../../scripts/testing/test-run-context.mjs';

const skip=shouldSkipRealStack()?'enable FULLNET_RUN_TEMPLATE_REAL_STACK=1':!areBundleInputsClean()?'source bundle inputs have uncommitted changes':false;
if((process.env.CI==='true'||process.env.CI==='1')&&skip)throw new Error('Required import recovery acceptance cannot skip: '+skip);
test('独立企业应用多行检查点、真实Worker强制退出和回执重试（双库）',{skip,timeout:concentratedAcceptanceTimeout+20*60000},async context=>{
 const prepared=await withTestRun({cwd:process.cwd(),disposableBuild:true,signal:context.signal},()=>prepareCreatedImportRecovery({signal:context.signal}));
 await runConcentratedAcceptance(context,async signal=>{
  for(const provider of ['sqlserver','mysql'])await verifyCreatedImportRecovery(provider,prepared,{signal});
 },{executionTimeoutMs:20*60000});
});
