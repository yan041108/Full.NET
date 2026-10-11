import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import {randomUUID} from 'node:crypto';
import {execFileSync} from 'node:child_process';
import {runNativeSuite} from '../../scripts/testing/native-suite-runner.mjs';

const parent=path.resolve('.tmp/native-suite-runner-fixtures');
const xml=outcome=>`<TestRun><Results><UnitTestResult testId="case" testName="case" outcome="${outcome}"/></Results></TestRun>`;
function fixture(){
 fs.mkdirSync(parent,{recursive:true});const cwd=fs.mkdtempSync(path.join(parent,'case-')),token=randomUUID();
 fs.writeFileSync(path.join(cwd,'.owner'),token);fs.writeFileSync(path.join(cwd,'.gitignore'),'.tmp/\nbin/\nlocks/\nartifacts/\n');
 fs.writeFileSync(path.join(cwd,'sample.csproj'),'<Project />');execFileSync('git',['init','--quiet'],{cwd,windowsHide:true});
 const matrix={integration:{assembly:'bin/sample.dll'},one:{project:'sample.csproj',filter:'one',minimum:1,timeout:'1m'},two:{project:'sample.csproj',filter:'two',minimum:1,timeout:'1m'}};
 fs.mkdirSync(path.join(cwd,'eng/testing'),{recursive:true});fs.writeFileSync(path.join(cwd,'eng/testing/test-matrix.json'),JSON.stringify(matrix));
 let builds=0,executions=0;let outcome='Passed',discovered=[{type:{typeName:'RequiredType'}}],emitReport=true;const environments=[],buildEnvironments=[];
 const runCommand=async(args,env)=>{
  environments.push(env);
  if(args[0]==='build'){builds++;buildEnvironments.push(env);fs.mkdirSync(path.join(cwd,'bin'),{recursive:true});fs.writeFileSync(path.join(cwd,'bin/sample.dll'),'compiled');return '';}
  if(args.includes('--list-tests'))return JSON.stringify({tests:discovered});
  executions++;if(emitReport){const dir=args[args.indexOf('--results-directory')+1],file=args[args.indexOf('--report-trx-filename')+1];fs.mkdirSync(dir,{recursive:true});fs.writeFileSync(path.join(dir,file),xml(outcome));}return '';
 };
 const run=(suite='one',options={},runOptions={})=>runNativeSuite({suite,results:'artifacts/results',trx:suite+'.trx',label:'测试 Native',...options},{cwd,platform:'linux',sdkVersion:'test-sdk',lockRoot:path.join(cwd,'locks'),runCommand,...runOptions});
 const clean=()=>{assert.equal(fs.realpathSync(path.dirname(cwd)),fs.realpathSync(parent));assert.ok(path.basename(cwd).startsWith('case-'));assert.equal(fs.readFileSync(path.join(cwd,'.owner'),'utf8'),token);fs.rmSync(cwd,{recursive:true});};
 return {cwd,matrix,run,clean,environments,buildEnvironments,get builds(){return builds;},get executions(){return executions;},set outcome(value){outcome=value;},set discovered(value){discovered=value;},set emitReport(value){emitReport=value;}};
}
test('同一消费者的多个 Native 专项只构建一次，各自保留实际验收结果',async()=>{
 const f=fixture();try{assert.equal((await f.run()).nativeVerified,true);assert.equal((await f.run('two')).nativeVerified,true);assert.equal(f.builds,1);assert.equal(f.executions,2);assert.ok(fs.existsSync(path.join(f.cwd,'artifacts/results/one.trx')));assert.ok(fs.existsSync(path.join(f.cwd,'artifacts/results/two.trx')));for(const env of f.environments)assert.equal(env.UseSharedCompilation,'false');}finally{f.clean();}
});
test('不同 pnpm 生命周期事件的 Native 专项仍共享构建，调用环境保持不变',async()=>{
 const f=fixture(),before=process.env.npm_lifecycle_event;
 try{process.env.npm_lifecycle_event='test:aot:native:e2e';await f.run();process.env.npm_lifecycle_event='test:aot:native:worker:e2e';await f.run('two');assert.equal(f.builds,1);for(const env of f.buildEnvironments)assert.equal(env.npm_lifecycle_event,undefined);assert.equal(process.env.npm_lifecycle_event,'test:aot:native:worker:e2e');}
 finally{if(before===undefined)delete process.env.npm_lifecycle_event;else process.env.npm_lifecycle_event=before;f.clean();}
});
test('最低发现数及必需测试类型不足时禁止执行',async()=>{
 const f=fixture();try{f.discovered=[];await assert.rejects(f.run(),/发现数不足/);assert.equal(f.executions,0);f.discovered=[{type:{typeName:'Other'}}];f.matrix.one.requiredTestTypes=['RequiredType'];fs.writeFileSync(path.join(f.cwd,'eng/testing/test-matrix.json'),JSON.stringify(f.matrix));await assert.rejects(f.run(),/缺少必需用例/);assert.equal(f.executions,0);}finally{f.clean();}
});
test('原生命令零退出仍不能把跳过结果登记为通过',async()=>{
 const f=fixture();try{f.outcome='Skipped';await assert.rejects(f.run(),/存在 Skipped/);assert.equal(f.executions,1);}finally{f.clean();}
});
test('拒绝越出工作区的结果路径，不构建也不执行',async()=>{
 const f=fixture();try{await assert.rejects(f.run('one',{results:'../outside'}),/结果路径必须位于工作区/);assert.equal(f.builds,0);assert.equal(f.executions,0);}finally{f.clean();}
});
test('零退出但没有新 TRX 时不能复用历史成功报告',async()=>{
 const f=fixture();try{await f.run();f.emitReport=false;await assert.rejects(f.run(),{code:'ENOENT'});assert.equal(f.executions,2);assert.equal(fs.existsSync(path.join(f.cwd,'artifacts/results/one.trx')),false);}finally{f.clean();}
});
test('企业非 Linux 只发现，不执行且不声明原生通过',async()=>{
 const f=fixture();try{const result=await f.run('one',{discoveryOnlyOnNonLinux:true},{platform:'win32'});assert.equal(result.nativeVerified,false);assert.equal(f.executions,0);assert.equal(f.builds,1);}finally{f.clean();}
});
test('保留非 Linux 跳过门禁，但不能声明原生通过',async()=>{
 const f=fixture();try{f.outcome='Skipped';const result=await f.run('one',{}, {platform:'win32'});assert.equal(result.nativeVerified,false);assert.equal(f.executions,1);}finally{f.clean();}
});
