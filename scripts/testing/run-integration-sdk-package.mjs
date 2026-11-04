import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { withTestRun, testRunEnvironment } from './test-run-context.mjs';
import { generateIntegrationSdk } from '../openapi/generate-integration-sdk.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../..');
function run(command,args,cwd=root,env=process.env) {
  const result=spawnSync(command,args,{cwd,env,encoding:'utf8',windowsHide:true,timeout:600_000,maxBuffer:20*1024*1024});
  if(result.error)throw result.error;if(result.status!==0)throw Error(((result.stdout??'')+(result.stderr??'')).slice(-8000));return result.stdout??'';
}
await generateIntegrationSdk();
run(process.execPath,[path.join(root,'packages/integration-sdk/node_modules/typescript/bin/tsc'),'--project',path.join(root,'packages/integration-sdk/tsconfig.json')]);
process.stdout.write(run(process.execPath,['--test','tests/integration-sdk.test.mjs']).slice(-2500));
// 与项目定向验收共用资源预算；此入口复用按开发入门已构建的后端引用，只编译当前测试程序集。
const build=run(process.execPath,['scripts/testing/run-build-with-resources.mjs','--cpu','2','--memory-gib','6','--','dotnet','build','tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj','-c','Release','--no-restore','-m:2','-v:minimal','-p:BuildProjectReferences=false','-p:UseSharedCompilation=false']);
process.stdout.write(build.slice(-1200));
await withTestRun({cwd:root,heavy:true},async()=>{
  const env=testRunEnvironment(),workspace=path.join(env.TEMP,'sdk-consumer'),packages=path.join(env.TEMP,'packages');fs.mkdirSync(workspace,{recursive:true});fs.mkdirSync(packages,{recursive:true});
  const prefix=path.dirname(process.execPath),npm=[path.join(prefix,'node_modules/npm/bin/npm-cli.js'),path.resolve(prefix,'../lib/node_modules/npm/bin/npm-cli.js')].find(file=>fs.existsSync(file));
  if(!npm)throw Error('The Node.js npm CLI is required for the isolated SDK package test.');
  const packed=JSON.parse(run(process.execPath,[npm,'pack','.','--ignore-scripts','--json','--pack-destination',packages],path.join(root,'packages/integration-sdk'),env));
  const inventory=new Set(packed[0].files.map(file=>file.path));
  for(const file of ['dist/index.js','dist/index.d.ts','README.md','LICENSE'])if(!inventory.has(file))throw Error('SDK package is missing '+file);
  const archive=path.join(packages,packed[0].filename);fs.writeFileSync(path.join(workspace,'package.json'),JSON.stringify({name:'fullnet-sdk-clean-consumer',private:true,type:'module'}));
  run(process.execPath,[npm,'install','--offline','--ignore-scripts','--no-audit','--no-fund','--package-lock=false',archive],workspace,env);
  for(const name of ['consumer.mjs','consumer.ts'])fs.copyFileSync(path.join(root,'tests/integration-sdk',name),path.join(workspace,name));
  fs.writeFileSync(path.join(workspace,'tsconfig.json'),JSON.stringify({compilerOptions:{target:'ES2024',module:'NodeNext',moduleResolution:'NodeNext',strict:true,noEmit:true,lib:['ES2024','DOM','DOM.Iterable']},include:['consumer.ts']}));
  run(process.execPath,[path.join(root,'packages/integration-sdk/node_modules/typescript/bin/tsc'),'--project',path.join(workspace,'tsconfig.json')],workspace,env);
  const installed=JSON.parse(fs.readFileSync(path.join(workspace,'node_modules/@fullnet/integration-sdk/package.json'),'utf8'));if(installed.dependencies&&Object.keys(installed.dependencies).length)throw Error('The SDK must not depend on workspace runtime implementations.');
  console.log(`SDK package verified: ${packed[0].size} compressed bytes, ${packed[0].files.length} files.`);
  const matrix=JSON.parse(fs.readFileSync(path.join(root,'eng/testing/test-matrix.json'),'utf8')).integration.shards['sdk-package'];
  const output=run('dotnet',['tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll','--filter',matrix.filter,'--minimum-expected-tests',String(matrix.minimum),'--no-ansi','--progress','off','--report-trx','--results-directory',env.TEMP],root,{...env,FULLNET_SDK_CONSUMER_DIRECTORY:workspace});
  fs.writeFileSync(path.join(env.TEMP,'sdk-package.log'),output);process.stdout.write(output.slice(-5000));console.log('SDK local pack, offline install and isolated TypeScript consumer passed.');
});
