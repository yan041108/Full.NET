import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CRUD_ARTIFACTS } from './application-crud-generation.mjs';
import { MODULE_ARTIFACTS } from './application-crud-module.mjs';

// 仅在本验收已接线的隔离模块注册授权贡献者，复用Host CLI的候选编译，不进入Vue或数据层。
export function verifyApplicationCrudAuthorization(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-authorization'),
} = {}) {
  const moduleRoot = 'src/Demo.Modules.Catalog';
  const contributor = moduleRoot + '/CatalogAuthorizationContributor.cs';
  const entry = moduleRoot + '/CatalogModule.cs';
  const project = moduleRoot + '/Demo.Modules.Catalog.csproj';
  const target = 'verification/CrudGeneration/authorization-target.json';
  for (const path of [contributor, target]) assert.equal(existsSync(join(appRoot, path)), false, 'authorization acceptance requires an unused path: ' + path);
  const entrySource = readFileSync(join(appRoot, entry), 'utf8');
  const anchor = 'services.AddOptions();';
  assert.equal(entrySource.split(anchor).length - 1, 1, 'authorization acceptance requires one module registration anchor');
  const targetData = JSON.parse(readFileSync(join(appRoot, 'verification/CrudGeneration/module-target.json'), 'utf8'));
  assert.equal(Object.hasOwn(targetData, 'clientRoute'), false, 'authorization acceptance must not enter client routing');
  writeFileSync(join(appRoot, contributor), readFileSync(new URL('./fixtures/application-catalog-authorization-contributor.cs.fixture', import.meta.url)));
  // 权威目录由Singleton工厂物化；贡献者无请求状态，不能注册为Scoped再由根容器解析。
  writeFileSync(join(appRoot, entry), entrySource.replace(anchor, anchor
    + '\n        services.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();'));
  writeFileSync(join(appRoot, target), JSON.stringify({ ...targetData, authorizationContributorPath: contributor }, null, 2));
  const protectedPaths = [...CRUD_ARTIFACTS, ...MODULE_ARTIFACTS.map((path) => moduleRoot + '/' + path),
    moduleRoot + '/.fullnet/codegeneration-manifest.json', entry, project, moduleRoot + '/Product.manual.cs',
    'src/Demo.Composition/Demo.Composition.csproj', 'src/Demo.Composition/ApplicationModuleCatalog.cs',
    'src/Demo.Host.Api/Program.cs', 'src/Demo.Host.Api/Demo.Host.Api.csproj', 'src/Demo.Host.Api/appsettings.json',
    'ui/admin/src/router/index.ts', '.fullnet/codegeneration-manifest.json', 'backend/Product.manual.cs',
    'appsettings.json', 'fullnet-app.json', 'framework-manifest.json', 'verification/CrudGeneration/schema.json',
    'verification/CrudGeneration/module-target.json', target];
  const capture = (paths) => new Map(paths.map((path) => [path, readFileSync(join(appRoot, path))]));
  const original = capture(protectedPaths);
  mkdirSync(reportDirectory, { recursive: true });
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, 'apply-host-integration', '--schema', join(appRoot, 'verification/CrudGeneration/schema.json'),
    '--repository', appRoot, '--target', join(appRoot, target)];
  const execute = (stage) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args,
      status: result.status, signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    assert.deepEqual((result.stdout ?? '').split(/\r?\n/u).filter(Boolean), ['Applied HostIntegration ' + project], 'incomplete authorization result');
    assert.deepEqual(capture(protectedPaths), original, stage + ' changed protected application content');
  };
  execute('apply');
  const content = readFileSync(join(appRoot, contributor), 'utf8');
  assert.ok(content.includes('new PermissionDefinition("catalog.manual.read", "人工权限", AuthorizationScope.Tenant)'), 'manual permission lost');
  const block = (name) => {
    const start = '// <fullnet-generated catalog.product ' + name + '>';
    const end = '// </fullnet-generated catalog.product ' + name + '>';
    assert.equal(content.split(start).length - 1, 1, 'generated block start missing or duplicated');
    assert.equal(content.split(end).length - 1, 1, 'generated block end missing or duplicated');
    assert.ok(content.indexOf(start) < content.indexOf(end), 'generated block boundaries reversed');
    return content.slice(content.indexOf(start) + start.length, content.indexOf(end));
  };
  const permissions = block('permissions');
  assert.equal((permissions.match(/new PermissionDefinition\(/gu) ?? []).length, 4);
  assert.equal((permissions.match(/AuthorizationScope\.Tenant/gu) ?? []).length, 4);
  assert.doesNotMatch(permissions, /AuthorizationScope\.Host/u);
  for (const name of ['Read', 'Create', 'Update', 'Disable']) {
    assert.equal((permissions.match(new RegExp('ProductPermissions\\.' + name + '\\b', 'gu')) ?? []).length, 1);
  }
  assert.equal((block('navigation').match(/new NavigationDefinition\(/gu) ?? []).length, 1);
  assert.equal((block('actions').match(/new AuthorizationActionDefinition\(/gu) ?? []).length, 3);
  // 追加本验收的人工内容，重复整链必须连同生成区块一起保持字节不变。
  writeFileSync(join(appRoot, contributor), content + '\n// 人工授权注释，重复接入不得改写。\n');
  const integrated = capture([...protectedPaths, contributor]);
  execute('repeat');
  assert.deepEqual(capture([...protectedPaths, contributor]), integrated, 'repeat changed authorization or application content');
  const result = { contributorIntegrated: true, repeatUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
