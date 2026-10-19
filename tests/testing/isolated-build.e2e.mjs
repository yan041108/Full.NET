import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdir, mkdtemp, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { parseBuildArguments, runBuild } from '../../scripts/testing/run-build-with-resources.mjs';
import { prepareIsolatedBuild } from '../../scripts/testing/isolated-build-output.mjs';

// 真实 restore/compiler/ProjectReference 能发现仅隔离顶层 DLL、仍争用共享 obj 的伪并发。
test('真实 .NET：同一源码的三个构建同时编译，公共依赖输出完全隔离', { timeout: 60000 }, async t => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'fullnet-real-parallel-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const source = path.join(root, 'source'), events = path.join(root, 'events');
  await mkdir(source); await mkdir(events);
  await writeFile(path.join(source, 'NuGet.Config'), '<configuration><packageSources><clear /></packageSources></configuration>');
  await writeFile(path.join(source, 'Directory.Build.props'), '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>');
  const shared = path.join(source, 'Shared'); await mkdir(shared);
  await writeFile(path.join(shared, 'Shared.csproj'), '<Project Sdk="Microsoft.NET.Sdk"/>');
  await writeFile(path.join(shared, 'Shared.cs'), 'public class SharedValue { public static int Value => 42; }');
  const hold = path.join(root, 'hold.mjs');
  await writeFile(hold, `import {writeFile,readdir} from 'node:fs/promises';
    const [directory,name]=process.argv.slice(2);
    await writeFile(directory+'/'+name+'.start',String(Date.now()));
    const deadline=Date.now()+20000;
    while((await readdir(directory)).filter(n=>n.endsWith('.start')).length<3){
      if(Date.now()>deadline)throw Error('Three actual compile targets did not overlap');
      await new Promise(resolve=>setTimeout(resolve,25));
    }
    await writeFile(directory+'/'+name+'.end',String(Date.now()));`);
  const xml = value => value.replaceAll('&', '&amp;').replaceAll('"', '&quot;');
  for (let id = 1; id <= 3; id += 1) {
    const app = path.join(source, `App${id}`); await mkdir(app);
    const command = `"${process.execPath}" "${hold}" "${events}" "App${id}"`;
    await writeFile(path.join(app, `App${id}.csproj`), `<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><ProjectReference Include="../Shared/Shared.csproj"/></ItemGroup><Target Name="RecordConcurrentCompile" BeforeTargets="CoreCompile"><Exec Command="${xml(command)}"/></Target></Project>`);
    await writeFile(path.join(app, 'App.cs'), `public class App${id} { public int Value => SharedValue.Value; }`);
  }
  const outcomes = await Promise.allSettled([1, 2, 3].map(id => runBuild([
    '--cwd', source, '--isolated-output', path.join(root, `out${id}`), '--cpu', '1', '--memory-gib', '1',
    '--', 'dotnet', 'build', `App${id}/App${id}.csproj`, '--configuration', 'Release', '--nologo', '--verbosity', 'quiet'
  ], { lockRoot: path.join(root, 'locks'), resourceProbe: async () => ({ cpuCount: 20, freePhysicalGiB: 48, freeCommitGiB: 48 }) })));
  for (const result of outcomes) assert.equal(result.status, 'fulfilled', result.reason?.message);
  const starts = await Promise.all([1, 2, 3].map(id => readFile(path.join(events, `App${id}.start`), 'utf8').then(Number)));
  const ends = await Promise.all([1, 2, 3].map(id => readFile(path.join(events, `App${id}.end`), 'utf8').then(Number)));
  assert.ok(Math.max(...starts) <= Math.min(...ends), '三个真实编译阶段须有重叠');
  for (let id = 1; id <= 3; id += 1) {
    await stat(path.join(root, `out${id}`, 'bin', 'Shared', 'release', 'Shared.dll'));
    await stat(path.join(root, `out${id}`, 'obj', 'Shared', 'project.assets.json'));
    await assert.rejects(stat(path.join(source, `App${id}`, 'bin')), { code: 'ENOENT' });
    await assert.rejects(stat(path.join(source, `App${id}`, 'obj')), { code: 'ENOENT' });
  }
  await assert.rejects(stat(path.join(shared, 'obj')), { code: 'ENOENT' });
});
