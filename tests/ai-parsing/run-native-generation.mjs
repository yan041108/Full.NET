import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {createHash,randomUUID} from 'node:crypto';
import {readFile,writeFile,mkdir,appendFile,realpath} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {withTestRun,enterHeavyTestPhase} from '../../scripts/testing/test-run-context.mjs';
import {startBoundedResourceReaper} from '../../scripts/testing/bounded-testcontainers-reaper.mjs';
import {validateNativeAotRuntimeResults} from '../../scripts/testing/native-aot-runtime-results.mjs';

// 只允许当前工作树与显式临时输出；不修改宿主 DNS、证书库或生产网络策略。
const [argument,resultsDirectory,...extra]=process.argv.slice(2);
const isolatedSuites=new Map([
  ['--chat',{key:'nativeChatRuntimeIntegration',report:'native-chat.trx',label:'chat'}],
  ['--search-answer',{key:'nativeKnowledgeSearchAnswerRuntimeIntegration',report:'native-search-answer.trx',label:'search-answer'}],
  ['--knowledge-models',{key:'nativeKnowledgeModelRuntimeIntegration',report:'native-knowledge-models.trx',label:'knowledge-models'}],
  ['--faq-provenance',{key:'nativeKnowledgeFaqProvenanceRuntimeIntegration',report:'native-faq-provenance.trx',label:'faq-provenance'}],
  ['--evaluation',{key:'nativeKnowledgeEvaluationRuntimeIntegration',report:'native-evaluation.trx',label:'evaluation'}],
  ['--structured',{key:'nativeKnowledgeStructuredRuntimeIntegration',report:'native-structured.trx',label:'structured'}],
]);
const isolated=isolatedSuites.get(argument);
const structured=argument==='--structured';
if(!argument||extra.length||(isolated?!resultsDirectory:argument.startsWith('--')||resultsDirectory!==undefined))
  throw new Error(`用法：run-native-generation.mjs <生成配置.json> | (${[...isolatedSuites.keys()].join(' | ')}) <仓库外的全新结果目录>`);
const repository=await realpath(fileURLToPath(new URL('../..',import.meta.url)));
const matrix=JSON.parse(await readFile(path.join(repository,'eng/testing/test-matrix.json'),'utf8'));
const gate=matrix[isolated?.key??'nativeKnowledgeGenerationIntegration'];
const structuredDriver=structured?process.env.FULLNET_NATIVE_STRUCTURED_SDK_IMAGE:undefined;
const config=isolated?{
  outputDirectory:resultsDirectory,
  apiPublishDirectory:process.env.FULLNET_NATIVE_AOT_PUBLISH_DIR??path.dirname(path.join(repository,matrix.nativeAotPublish.executableRelativePath)),
  assembly:await realpath(path.join(repository,matrix.integration.assembly)),
  sdkImage:structuredDriver??'mcr.microsoft.com/dotnet/sdk:10.0.404',
  parserImage:structured?process.env.FULLNET_NATIVE_STRUCTURED_PARSER_IMAGE:undefined,
}:JSON.parse(await readFile(argument,'utf8'));
if(!Number.isSafeInteger(gate.minimum)||gate.minimum<1||!/^[\w.~|&!=]+$/.test(gate.filter)||!/^\d+m$/.test(gate.timeout))throw new Error('原生模型验证矩阵不合法');
const output=path.resolve(config.outputDirectory);
if(output===repository||output.startsWith(repository+path.sep))throw new Error('原生验收输出必须位于仓库外');
const mounts=isolated?['apiPublishDirectory']:['apiPublishDirectory','workerPublishDirectory','fixturesDirectory','nugetDirectory'];
for(const key of mounts)config[key]=await realpath(config[key]);
if((!isolated||structured)&&!/^sha256:[a-f0-9]{64}$/.test(config.parserImage))
  throw new Error('需要不可变解析镜像摘要');
await mkdir(output,{recursive:!isolated});
const execute=promisify(execFile),owner=randomUUID();
const docker=async args=>(await execute('docker',args,{windowsHide:true,maxBuffer:16*1024*1024})).stdout;
const journal=entry=>appendFile(path.join(output,'resources.jsonl'),JSON.stringify(entry)+'\n');
const report=isolated?.report??'native-generation.trx';
const structuredTools=[];
if(structured&&!structuredDriver){
  if(process.platform!=='linux')
    throw new Error('非 Linux 宿主请通过 FULLNET_NATIVE_STRUCTURED_SDK_IMAGE 指定包含 Node 和 Docker CLI 的 Linux SDK 镜像');
  const {stdout}=await execute('sh',['-c','command -v docker']);
  for(const [name,tool] of [['node',process.execPath],['docker',stdout.trim()]])
    structuredTools.push('--mount',`type=bind,source=${await realpath(tool)},target=/usr/local/bin/${name},readonly`);
}

async function prepareStructuredFixtures(labels){
  const generator=path.join(repository,'tests/ai-parsing/generate-correction-fixtures.py');
  const generated=JSON.parse(await docker(['run','--pull=never','--rm',...labels,'--network','none','--read-only',
    '--cap-drop','ALL','--security-opt','no-new-privileges','--memory','512m','--cpus','1','--pids-limit','64',
    '--tmpfs','/tmp:rw,noexec,nosuid,size=16m,uid=65532,gid=65532,mode=0700',
    '--mount',`type=bind,source=${generator},target=/fixture-generator.py,readonly`,
    '--entrypoint','/opt/ocr/bin/python',config.parserImage,'-B','/fixture-generator.py','/tmp/fixtures','--emit-base64']));
  const names=['correction-ocr.png','correction-table.csv'];
  if(!Array.isArray(generated)||generated.map(item=>item.name).sort().join(',')!==names.join(','))
    throw new Error('结构化来源生成器未提供精确的 CSV 与 OCR 文件');
  const fixtures=path.join(output,'fixtures');
  await mkdir(fixtures);
  for(const {name,data,bytes,sha256} of generated){
    const content=Buffer.from(data,'base64');
    if(content.length!==bytes||createHash('sha256').update(content).digest('hex')!==sha256)
      throw new Error(`结构化来源摘要不符：${name}`);
    await writeFile(path.join(fixtures,name),content,{flag:'wx'});
  }
  await writeFile(path.join(fixtures,'correction-fixtures.json'),
    JSON.stringify(generated.map(({name,bytes,sha256})=>({name,bytes,sha256})),null,2));
}

const controllerPath=isolated?path.relative(repository,config.assembly).split(path.sep).join('/'):'';
if(isolated&&(!/^tests\/[\w./-]+\.dll$/.test(controllerPath)||controllerPath.split('/').includes('..')))
  throw new Error('消费者程序集必须位于本次挂载的仓库中');
const prepareController=isolated?`
# 复用上一步编译的消费者程序集，挂载为只读。
assembly=/src/${controllerPath}`:`
export FULLNET_NATIVE_KNOWLEDGE_FIXTURES=/fixtures FULLNET_NATIVE_KNOWLEDGE_NODE=/usr/local/bin/node
export FULLNET_NATIVE_KNOWLEDGE_CLI=/src/scripts/ai/document-sandbox-cli.mjs
export FULLNET_WORKER_NATIVE_AOT_PUBLISH_DIR=/native-worker
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -m:2 2>&1 | tee /out/build.log
assembly=/work/build/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll`;
const runtime=`set -euo pipefail
export DOTNET_PROCESSOR_COUNT=2 NUGET_PACKAGES=/root/.nuget/packages
export UseArtifactsOutput=true ArtifactsPath=/work/build
mkdir -p /work/tls /out/results
trap 'for logs in /tmp/fullnet-native-api-logs /tmp/fullnet-native-worker-logs; do
  if [ -d "$logs" ]; then cp -r "$logs" /out/results/; fi
done' EXIT
for item in Full.NET.slnx Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config nuget.config src; do
  if [ -e "/src/$item" ]; then ln -s "/src/$item" "/work/$item"; fi
done
# CA 与 DNS 均只存在于本批自动移除的 SDK 容器；正式宿主仍进行完整 TLS 验证。
openssl req -x509 -newkey rsa:2048 -nodes -keyout /work/tls/key.pem -out /work/tls/ca.crt -days 1 -subj '/CN=api.openai.com' -addext 'subjectAltName=DNS:api.openai.com' -addext 'basicConstraints=critical,CA:TRUE' >/out/certificate-generation.log 2>&1
openssl pkcs12 -export -out /work/tls/server.pfx -inkey /work/tls/key.pem -in /work/tls/ca.crt -passout pass:fullnet-test-only
cp /work/tls/ca.crt /usr/local/share/ca-certificates/fullnet-native-model.crt
update-ca-certificates >/out/certificate-trust.log 2>&1
export FULLNET_NATIVE_MODEL_ADDRESS=93.184.216.2 FULLNET_NATIVE_MODEL_CERTIFICATE=/work/tls/server.pfx
export FULLNET_NATIVE_AOT_PUBLISH_DIR=/native-api
cd /src
${prepareController}
filter='${gate.filter}'
dotnet "$assembly" --list-tests json --no-ansi --filter "$filter" --results-directory /out/results > /out/results/discovery.json
dotnet "$assembly" --no-ansi --progress off --timeout ${gate.timeout} --filter "$filter" --minimum-expected-tests ${gate.minimum} --results-directory /out/results --report-trx --report-trx-filename ${report} 2>&1 | tee /out/tests.log
`;
await writeFile(path.join(output,'runtime.sh'),runtime);
await withTestRun({cwd:output,buildBudget:{cpu:2,memoryGiB:0.5}},async()=>{
 await enterHeavyTestPhase();
 let id,broken=false;
 const stopOwned=async()=>{
  if(!id||!(await docker(['container','ls','-aq','--filter',`id=${id}`])).trim())return;
  const [actual]=JSON.parse(await docker(['inspect',id]));
  if(actual.Id!==id||actual.Config.Labels['fullnet.native-generation-owner']!==owner)throw new Error('运行容器归属不匹配');
  await docker(actual.State.Running?['stop','--time','10',id]:['rm',id]);
 };
 const reaper=await startBoundedResourceReaper({docker,journal,onDisconnect:()=>{broken=true;stopOwned().catch(error=>console.error(error.message));}});
 try{
  const labels=['--label',`org.testcontainers.resource-reaper-session=${reaper.session}`,'--label',`fullnet.bounded-owner=${reaper.session}`];
  if(structured)await prepareStructuredFixtures(labels);
  // 内部网络只模拟目的地身份，不连接公网服务；DNS、端口与本容器地址逐项核证。
  const network=(await docker(['network','create','--internal','--subnet','93.184.216.0/29',...labels,`fullnet-native-model-${owner}`])).trim();
  const [actualNetwork]=JSON.parse(await docker(['network','inspect',network]));
  if(actualNetwork.Id!==network||!actualNetwork.Internal||actualNetwork.Labels['fullnet.bounded-owner']!==reaper.session||actualNetwork.IPAM.Config[0].Subnet!=='93.184.216.0/29')throw new Error('模型网络归属或隔离不符');
  await journal({stage:'model-network',network,session:reaper.session,internal:true});
  const env={...reaper.environment,TESTCONTAINERS_HOST_OVERRIDE:'host.docker.internal'};
  if(structured)Object.assign(env,{FULLNET_K02_PARSER_IMAGE:config.parserImage,FULLNET_K02_STRUCTURED_FIXTURES:'/out/fixtures'});
  if(!isolated)env.FULLNET_NATIVE_KNOWLEDGE_IMAGE=config.parserImage;
  const generationMounts=isolated?[]
   : ['--mount',`type=bind,source=${config.workerPublishDirectory},target=/native-worker,readonly`,
      '--mount',`type=bind,source=${config.fixturesDirectory},target=/fixtures,readonly`,
      '--mount',`type=bind,source=${config.nugetDirectory},target=/root/.nuget/packages`];
  id=(await docker(['create','--pull=never','--rm',...labels,'--label',`fullnet.native-generation-owner=${owner}`,
   '--memory','6g','--memory-swap','6g','--cpus','2','--pids-limit','512',
   '--network',network,'--ip','93.184.216.2','--add-host','api.openai.com:93.184.216.2',
   ...(process.platform==='linux'?['--add-host','host.docker.internal:host-gateway']:[]),
   ...Object.entries(env).flatMap(([key,value])=>['--env',`${key}=${value}`]),
   '--mount',`type=bind,source=${repository},target=/src,readonly`,
   '--mount',`type=bind,source=${output},target=/out`,
   '--mount',`type=bind,source=${config.apiPublishDirectory},target=/native-api,readonly`,
   ...generationMounts,
   ...structuredTools,
   '--mount','type=bind,source=/var/run/docker.sock,target=/var/run/docker.sock',
   '--entrypoint','/bin/bash',config.sdkImage,'/out/runtime.sh'])).trim();
  if(!/^[a-f0-9]{64}$/.test(id))throw new Error('缺少精确运行容器 ID');
  // 第二张普通桥接网卡仅供数据库访问，官方模型域名仍固定回到内部网络的本容器。
  await docker(['network','connect','bridge',id]);
  const [actual]=JSON.parse(await docker(['inspect',id]));
  if(actual.Id!==id||actual.Config.Labels['fullnet.native-generation-owner']!==owner||actual.HostConfig.Memory!==6*1024**3||actual.HostConfig.NanoCpus!==2e9||!actual.HostConfig.ExtraHosts.includes('api.openai.com:93.184.216.2'))throw new Error('运行容器边界不符');
  await journal({stage:'runtime-created',id,owner});reaper.assertReady();
  await new Promise((resolve,reject)=>{const child=spawn('docker',['start','--attach',id],{stdio:'inherit',windowsHide:true});child.on('error',reject);child.on('close',code=>code===0?resolve():reject(new Error(`原生运行退出 ${code}`)));});
  if(broken)throw new Error('验收期间资源回收连接中断');
  const discovery=JSON.parse(await readFile(path.join(output,'results/discovery.json'),'utf8'));
  if(discovery.tests.length!==gate.minimum||gate.requiredTestTypes.some(type=>!discovery.tests.some(test=>test.type?.typeName===type)))throw new Error('原生生成影响集发现数或类型不符');
  const result=validateNativeAotRuntimeResults(await readFile(path.join(output,'results',report),'utf8'),discovery.tests.map(x=>x.uid));
  console.log(`Native ${isolated?.label??'generation'}: ${result.passed} passed, zero failure/skip`);
 }finally{await stopOwned();await reaper.close();}
});
