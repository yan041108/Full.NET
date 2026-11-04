import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { testRunEnvironment } from '../../../scripts/testing/test-run-context.mjs';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { CRUD_ARTIFACTS } from './application-crud-generation.mjs';
import { MODULE_ARTIFACTS, assertHostIntegrationReceipt } from './application-crud-module.mjs';

// 模块阶段已经完成授权与 DI 接入；这里只核对权威片段及人工漂移保护，不再重新认领受管入口。
export function verifyApplicationCrudAuthorization(appRoot, {
  run = spawnSync,
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-authorization'),
} = {}) {
  const moduleRoot = 'src/Demo.Modules.Catalog';
  const contributor = moduleRoot + '/CatalogAuthorizationContributor.cs';
  const entry = moduleRoot + '/CatalogModule.cs';
  const project = moduleRoot + '/Demo.Modules.Catalog.csproj';
  const target = 'verification/CrudGeneration/authorization-target.json';
  assert.equal(existsSync(join(appRoot, contributor)), true, 'complete integration must have created the contributor');
  assert.equal(existsSync(join(appRoot, target)), false, 'authorization acceptance requires an unused target path');
  const entrySource = readFileSync(join(appRoot, entry), 'utf8');
  const anchor = 'services.AddOptions();';
  assert.equal(entrySource.split(anchor).length - 1, 1, 'authorization acceptance requires one module registration anchor');
  const targetData = JSON.parse(readFileSync(join(appRoot, 'verification/CrudGeneration/module-target.json'), 'utf8'));
  assert.equal(Object.hasOwn(targetData, 'clientRoute'), false, 'authorization acceptance must not enter client routing');
  assert.equal(entrySource.split('services.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();').length - 1, 1);
  writeFileSync(join(appRoot, target), JSON.stringify({ ...targetData, authorizationContributorPath: contributor }, null, 2));
  const protectedPaths = [...CRUD_ARTIFACTS, ...MODULE_ARTIFACTS.map((path) => moduleRoot + '/' + path),
    entry, project, moduleRoot + '/Product.manual.cs',
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
  const execute = (stage, expectedStatus = 0) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, env: testRunEnvironment() });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ args,
      status: result.status, signal: result.signal, error: result.error?.message, stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, expectedStatus, `${stage} failed: ${result.stderr ?? ''}\n${result.stdout ?? ''}`);
    if (expectedStatus === 0) assertHostIntegrationReceipt(result.stdout, project);
    else assert.ok((result.stderr ?? '').includes(contributor), 'missing exact contributor conflict');
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
  // 追加本验收的人工内容后必须拒绝整链写入；不得把保留注释误当作自动重新认领授权。
  writeFileSync(join(appRoot, contributor), content + '\n// 人工授权注释，重复接入不得改写。\n');
  const integrated = capture([...protectedPaths, contributor]);
  execute('drift', 2);
  assert.deepEqual(capture([...protectedPaths, contributor]), integrated, 'repeat changed authorization or application content');
  writeFileSync(join(appRoot, contributor), content);
  execute('repeat');
  assert.equal(readFileSync(join(appRoot, contributor), 'utf8'), content, 'repeat changed contributor');
  const result = { contributorIntegrated: true, repeatUnchanged: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
