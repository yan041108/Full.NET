import {spawn,execFile} from 'node:child_process';
import {promisify} from 'node:util';
import {randomUUID} from 'node:crypto';
import {readFile,writeFile,mkdir,appendFile,realpath} from 'node:fs/promises';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {withTestRun,enterHeavyTestPhase} from '../../scripts/testing/test-run-context.mjs';
import {startBoundedResourceReaper} from '../../scripts/testing/bounded-testcontainers-reaper.mjs';
import {validateNativeAotRuntimeResults} from '../../scripts/testing/native-aot-runtime-results.mjs';

// 只允许当前工作树与显式临时输出；不修改宿主 DNS、证书库或生产网络策略。
const config=JSON.parse(await readFile(process.argv[2],'utf8'));
const repository=await realpath(fileURLToPath(new URL('../..',import.meta.url)));
const gate=JSON.parse(await readFile(path.join(repository,'eng/testing/test-matrix.json'),'utf8')).nativeKnowledgeGenerationIntegration;
if(!Number.isSafeInteger(gate.minimum)||gate.minimum<1||!/^[\w.~|&!=]+$/.test(gate.filter)||!/^\d+m$/.test(gate.timeout))throw new Error('原生生成矩阵不合法');
const output=path.resolve(config.outputDirectory);
if(output===repository||output.startsWith(repository+path.sep))throw new Error('原生验收输出必须位于仓库外');
for(const key of ['apiPublishDirectory','workerPublishDirectory','fixturesDirectory','nugetDirectory'])config[key]=await realpath(config[key]);
if(!/^sha256:[a-f0-9]{64}$/.test(config.parserImage))throw new Error('需要不可变解析镜像摘要');
await mkdir(output,{recursive:true});
const execute=promisify(execFile),owner=randomUUID();
const docker=async args=>(await execute('docker',args,{windowsHide:true,maxBuffer:16*1024*1024})).stdout;
const journal=entry=>appendFile(path.join(output,'resources.jsonl'),JSON.stringify(entry)+'\n');
const runtime=`set -euo pipefail
export DOTNET_PROCESSOR_COUNT=2 NUGET_PACKAGES=/root/.nuget/packages
export UseArtifactsOutput=true ArtifactsPath=/work/build
mkdir -p /work/tls /out/results
trap 'if [ -d /work/artifacts ]; then cp -r /work/artifacts /out/results/process-logs; fi' EXIT
for item in Full.NET.slnx Directory.Build.props Directory.Build.targets Directory.Packages.props global.json NuGet.Config nuget.config src; do
  if [ -e "/src/$item" ]; then ln -s "/src/$item" "/work/$item"; fi
done
# CA 与 DNS 均只存在于本批自动移除的 SDK 容器；正式宿主仍进行完整 TLS 验证。
openssl req -x509 -newkey rsa:2048 -nodes -keyout /work/tls/key.pem -out /work/tls/ca.crt -days 1 -subj '/CN=api.openai.com' -addext 'subjectAltName=DNS:api.openai.com' -addext 'basicConstraints=critical,CA:TRUE' >/out/certificate-generation.log 2>&1
openssl pkcs12 -export -out /work/tls/server.pfx -inkey /work/tls/key.pem -in /work/tls/ca.crt -passout pass:fullnet-test-only
cp /work/tls/ca.crt /usr/local/share/ca-certificates/fullnet-native-model.crt
update-ca-certificates >/out/certificate-trust.log 2>&1
export FULLNET_NATIVE_MODEL_ADDRESS=93.184.216.2 FULLNET_NATIVE_MODEL_CERTIFICATE=/work/tls/server.pfx
export FULLNET_NATIVE_KNOWLEDGE_FIXTURES=/fixtures FULLNET_NATIVE_KNOWLEDGE_NODE=/usr/local/bin/node
export FULLNET_NATIVE_KNOWLEDGE_CLI=/src/scripts/ai/document-sandbox-cli.mjs
export FULLNET_NATIVE_AOT_PUBLISH_DIR=/native-api FULLNET_WORKER_NATIVE_AOT_PUBLISH_DIR=/native-worker
cd /src
dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release --nologo -m:2 2>&1 | tee /out/build.log
assembly=/work/build/bin/Full.NET.IntegrationTests/release/Full.NET.IntegrationTests.dll
filter='${gate.filter}'
dotnet "$assembly" --list-tests json --no-ansi --filter "$filter" > /out/results/discovery.json
dotnet "$assembly" --no-ansi --progress off --timeout ${gate.timeout} --filter "$filter" --minimum-expected-tests ${gate.minimum} --results-directory /out/results --report-trx --report-trx-filename native-generation.trx 2>&1 | tee /out/tests.log
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
  // 内部网络只模拟目的地身份，不连接公网服务；DNS、端口与本容器地址逐项核证。
  const network=(await docker(['network','create','--internal','--subnet','93.184.216.0/29',...labels,`fullnet-native-model-${owner}`])).trim();
  const [actualNetwork]=JSON.parse(await docker(['network','inspect',network]));
  if(actualNetwork.Id!==network||!actualNetwork.Internal||actualNetwork.Labels['fullnet.bounded-owner']!==reaper.session||actualNetwork.IPAM.Config[0].Subnet!=='93.184.216.0/29')throw new Error('模型网络归属或隔离不符');
  await journal({stage:'model-network',network,session:reaper.session,internal:true});
  const env={...reaper.environment,TESTCONTAINERS_HOST_OVERRIDE:'host.docker.internal',FULLNET_NATIVE_KNOWLEDGE_IMAGE:config.parserImage};
  id=(await docker(['create','--pull=never','--rm',...labels,'--label',`fullnet.native-generation-owner=${owner}`,
   '--memory','6g','--memory-swap','6g','--cpus','2','--pids-limit','512',
   '--network',network,'--ip','93.184.216.2','--add-host','api.openai.com:93.184.216.2',
   ...Object.entries(env).flatMap(([key,value])=>['--env',`${key}=${value}`]),
   '--mount',`type=bind,source=${repository},target=/src,readonly`,
   '--mount',`type=bind,source=${output},target=/out`,
   '--mount',`type=bind,source=${config.apiPublishDirectory},target=/native-api,readonly`,
   '--mount',`type=bind,source=${config.workerPublishDirectory},target=/native-worker,readonly`,
   '--mount',`type=bind,source=${config.fixturesDirectory},target=/fixtures,readonly`,
   '--mount',`type=bind,source=${config.nugetDirectory},target=/root/.nuget/packages`,
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
  const result=validateNativeAotRuntimeResults(await readFile(path.join(output,'results/native-generation.trx'),'utf8'),discovery.tests.map(x=>x.uid));
  console.log(`Native generation: ${result.passed} passed, zero failure/skip`);
 }finally{await stopOwned();await reaper.close();}
});
