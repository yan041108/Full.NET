import test from 'node:test';
import {areBundleInputsClean} from '../../scripts/templates/build-source-bundle.mjs';
import {shouldSkipRealStack} from './support/created-app-real-stack.mjs';
import {prepareCreatedImportRecovery,verifyCreatedImportRecovery} from './support/created-enterprise-import-recovery.mjs';
import {verifyApplicationImportSessionRevocation} from './support/application-import-session-revocation.mjs';
import {runConcentratedAcceptance,withTestRun} from '../../scripts/testing/test-run-context.mjs';

const skip=shouldSkipRealStack()?'enable FULLNET_RUN_TEMPLATE_REAL_STACK=1':!areBundleInputsClean()?'source bundle inputs have uncommitted changes':false;
if((process.env.CI==='true'||process.env.CI==='1')&&skip)throw new Error('Required import recovery acceptance cannot skip: '+skip);

// 准备与排队各自有界；共享资源等待不消耗双库业务预算，外层覆盖三个阶段和结束余量。
const preparationTimeoutMs=20*60_000,queueTimeoutMs=45*60_000,executionTimeoutMs=20*60_000;
test('独立企业应用检查点恢复、回执重试和会话撤销（一次构建集中双库）',{skip,timeout:preparationTimeoutMs+queueTimeoutMs+executionTimeoutMs+30_000},async context=>{
 const preparationSignal=AbortSignal.any([context.signal,AbortSignal.timeout(preparationTimeoutMs)]);
 const prepared=await withTestRun({cwd:process.cwd(),disposableBuild:true,signal:preparationSignal},()=>prepareCreatedImportRecovery({signal:preparationSignal}));
 await runConcentratedAcceptance(context,async signal=>{
  for(const provider of ['sqlserver','mysql']){
   await verifyCreatedImportRecovery(provider,prepared,{signal});
   // 会话场景使用独立运行目录；应用源码及三宿主产物原样复用，探针文件不串组。
   await verifyCreatedImportRecovery(provider,{...prepared,out:prepared.out+'/session-revocation'},{signal,verifyApplication:verifyApplicationImportSessionRevocation});
  }
 },{timeoutMs:queueTimeoutMs,executionTimeoutMs});
});
