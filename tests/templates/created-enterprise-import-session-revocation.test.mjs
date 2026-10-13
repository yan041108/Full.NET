import test from 'node:test';
import {areBundleInputsClean} from '../../scripts/templates/build-source-bundle.mjs';
import {shouldSkipRealStack} from './support/created-app-real-stack.mjs';
import {prepareCreatedImportRecovery,verifyCreatedImportRecovery} from './support/created-enterprise-import-recovery.mjs';
import {verifyApplicationImportSessionRevocation} from './support/application-import-session-revocation.mjs';
import {runConcentratedAcceptance,withTestRun} from '../../scripts/testing/test-run-context.mjs';

const skip=shouldSkipRealStack()?'enable FULLNET_RUN_TEMPLATE_REAL_STACK=1':!areBundleInputsClean()?'source bundle inputs have uncommitted changes':false;
if((process.env.CI==='true'||process.env.CI==='1')&&skip)throw new Error('Required session revocation acceptance cannot skip: '+skip);

// 整批实现后一次准备、一次排队、双库顺序执行，避免每个授权边界重新构建应用。
const preparationTimeoutMs=20*60_000,queueTimeoutMs=45*60_000,executionTimeoutMs=20*60_000;
test('独立企业应用排队/检查点会话撤销、新会话重试与任务隔离（双库）',{skip,timeout:preparationTimeoutMs+queueTimeoutMs+executionTimeoutMs+30_000},async context=>{
 const preparationSignal=AbortSignal.any([context.signal,AbortSignal.timeout(preparationTimeoutMs)]);
 const prepared=await withTestRun({cwd:process.cwd(),disposableBuild:true,signal:preparationSignal},()=>prepareCreatedImportRecovery({signal:preparationSignal}));
 await runConcentratedAcceptance(context,async signal=>{
  for(const provider of ['sqlserver','mysql'])await verifyCreatedImportRecovery(provider,prepared,{signal,verifyApplication:verifyApplicationImportSessionRevocation});
 },{timeoutMs:queueTimeoutMs,executionTimeoutMs});
});
