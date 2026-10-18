import assert from 'node:assert/strict';
import { execFile } from 'node:child_process';
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { promisify } from 'node:util';
import test from 'node:test';
import { testRunEnvironment, withTestRun } from '../../scripts/testing/test-run-context.mjs';

const exec = promisify(execFile);
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const sources = [
  'Reporting/ReportingExportOptions', 'Notifications/DingTalkApprovalSyncOptions',
  'Cryptography/CryptographyOptions', 'Mqtt/MqttBrokerOptions',
  'ObservabilityAdmin/ObservabilityAdminOptions', 'Platform/PlatformBackupExecutorOptions',
  'ImportExport/ImportExportOptions',
];
const groups = ['reporting', 'dingtalk', 'cryptography', 'mqtt', 'observability', 'backup', 'import',
  'defaults', 'reporting-invalid', 'dingtalk-invalid'];

test('正式模块配置在实际 AOT 源生成绑定下覆盖部署值并保留校验', { timeout: 180_000 }, async t => {
  const parent = path.join(root, '.tmp');
  await mkdir(parent, { recursive: true });
  const directory = await mkdtemp(path.join(parent, 'module-options-binding-'));
  try {
    const includes = sources.map(source => {
      const [module, name] = source.split('/');
      return `<Compile Include="../../src/Modules/Full.NET.Modules.${module}/Configuration/${name}.cs" Link="${name}.cs" />`;
    }).join('\n');
    await writeFile(path.join(directory, 'BindingProbe.csproj'), `<Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup><OutputType>Exe</OutputType></PropertyGroup>
      <ItemGroup><FrameworkReference Include="Microsoft.AspNetCore.App" />${includes}</ItemGroup>
    </Project>`);
    await writeFile(path.join(directory, 'Program.cs'), await readFile(new URL('./fixtures/module-options-binding.cs.fixture', import.meta.url)));
    await withTestRun({ cwd: directory, disposableBuild: true, signal: t.signal }, async () => {
      const options = { cwd: directory, env: { ...testRunEnvironment(), DOTNET_PROCESSOR_COUNT: '2' },
        windowsHide: true, signal: t.signal, maxBuffer: 4 * 1024 * 1024 };
      const build = await exec('dotnet', ['build', 'BindingProbe.csproj', '-c', 'Release', '--nologo',
        '-p:FullNetAotAnalysis=true', '-p:EmitCompilerGeneratedFiles=true',
        '-p:CompilerGeneratedFilesOutputPath=obj/generated', '-p:UseSharedCompilation=false', '-nodeReuse:false'], options);
      assert.doesNotMatch(build.stdout + build.stderr, /\bwarning\s+[A-Z]+\d+/iu);
      // 确认真实生成了拦截器；纯 JIT 反射绑定通过不能替代这项回归。
      const generated = path.join(directory, 'obj/generated/Microsoft.Extensions.Configuration.Binder.SourceGeneration/Microsoft.Extensions.Configuration.Binder.SourceGeneration.ConfigurationBindingGenerator');
      const files = await readdir(generated);
      assert.ok(files.some(file => file.endsWith('.g.cs')), '缺少实际配置绑定源生成器产物');
      const result = await exec('dotnet', ['bin/Release/net10.0/BindingProbe.dll'], options);
      const lines = result.stdout.trim().split(/\r?\n/u);
      assert.equal(lines.length, groups.length);
      for (const group of groups) {
        await t.test(group, () => assert.ok(lines.includes(`CHECK ${group} True`), `部署配置未生效或校验缺失：${group}`));
      }
    });
  } finally {
    // 只删除本测试新建且位于固定临时父目录下的目录，不能清理其它窗口的资源。
    assert.equal(path.dirname(directory), parent);
    await rm(directory, { recursive: true, force: true });
  }
});
