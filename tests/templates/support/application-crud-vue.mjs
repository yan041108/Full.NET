import assert from 'node:assert/strict';
import { copyFileSync, existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { spawnSync } from 'node:child_process';
import { CRUD_ARTIFACTS } from './application-crud-generation.mjs';
import { runPnpm } from './pnpm-process.mjs';

const generatedNames = ['guards.generated.ts', 'index.generated.ts', 'models.generated.ts', 'operations.generated.ts'];
const vueSources = new Map([
  ['clients/vue/products-page.generated.ts', 'ui/admin/src/views/products-page.generated.ts'],
  ['clients/vue/products.generated.ts', 'ui/admin/src/views/products.generated.ts'],
  ['clients/vue/productsView.vue', 'ui/admin/src/views/CatalogProductsView.vue'],
]);
const clientRoot = 'packages/client-contracts/src/application-generated/catalog-product';
const navigationCatalogPath = 'packages/client-contracts/src/navigation-catalog.ts';
const navigationKey = 'm7-catalog-products';
const navigationEntry = `  { componentKey: '${navigationKey}', routeName: '${navigationKey}', path: '/catalog/products' },\n`;
const operationExport = `export {
  catalogCreateProduct,
  catalogDeleteProduct,
  catalogGetProduct,
  catalogListProducts,
  catalogUpdateProduct
} from './application-generated/catalog-product/operations.generated.js';\n`;
const modelExport = `export type {
  CreateProductRequest,
  DeleteProductRequest,
  PagedResultOfProductResponse,
  ProductResponse,
  UpdateProductRequest
} from './application-generated/catalog-product/models.generated.js';\n`;

// 独立应用拥有客户端包与管理端骨架；业务生成文件只写入应用专属路径。
export function prepareApplicationCrudVue(appRoot) {
  const clientIndex = join(appRoot, 'packages/client-contracts/src/index.ts');
  const navigationCatalog = join(appRoot, navigationCatalogPath);
  const moduleTarget = join(appRoot, 'verification/CrudGeneration/module-target.json');
  const vueTarget = join(appRoot, 'verification/CrudGeneration/vue-target.json');
  const sources = [
    ...generatedNames.map((name) => [`verification/ClientGeneration/generated/${name}`, `${clientRoot}/${name}`]),
    ...vueSources,
  ];
  for (const path of [clientIndex, navigationCatalog, moduleTarget, join(appRoot, 'ui/admin/src/router/index.ts'),
    ...sources.map(([source]) => join(appRoot, source))]) {
    assert.equal(existsSync(path), true, 'application Vue source missing: ' + path);
  }
  for (const path of [vueTarget, ...sources.map(([, destination]) => join(appRoot, destination))]) {
    assert.equal(existsSync(path), false, 'Vue adoption requires an unused application path: ' + path);
  }
  const originalIndex = readFileSync(clientIndex, 'utf8');
  const originalNavigation = readFileSync(navigationCatalog, 'utf8');
  assert.equal(originalIndex.includes('catalogCreateProduct'), false, 'application client product operation already exported');
  // 按目录项的实际字段检查三种碰撞；动态值或特殊转义无法安全判定时拒绝采纳。
  const catalogStart = originalNavigation.indexOf('= [', originalNavigation.indexOf('export const ADMIN_NAVIGATION_CATALOG'));
  assert.ok(catalogStart >= 0, 'application navigation catalog declaration changed');
  assert.equal(originalNavigation.split('] as const;').length - 1, 1, 'application navigation catalog anchor changed');
  const navigationAnchor = originalNavigation.indexOf('] as const;');
  const catalogBody = originalNavigation.slice(catalogStart + 3, navigationAnchor);
  const entries = [...catalogBody.matchAll(/\{([^{}]*)\}/gu)];
  assert.ok(entries.length > 0, 'application navigation catalog is empty');
  assert.equal(catalogBody.replace(/\{[^{}]*\}/gu, '').replace(/[\s,]/gu, ''), '',
    'navigation catalog entry must use static fields');
  const fieldPattern = /\b(componentKey|routeName|path)\s*:\s*(['"])([^'"\\]*)\2/gu;
  const values = entries.flatMap(([, body]) => {
    const fields = [...body.matchAll(fieldPattern)];
    assert.deepEqual(fields.map(([, name]) => name).sort(), ['componentKey', 'path', 'routeName'],
      'navigation catalog entry must use static fields');
    assert.equal(body.replace(fieldPattern, '').replace(/[\s,]/gu, ''), '',
      'navigation catalog entry must use static fields');
    return fields.map(([, , , value]) => value);
  });
  assert.equal(values.some(value => value === navigationKey || value === '/catalog/products'),
    false, 'navigation key is already occupied');
  const precedingEntry = originalNavigation.slice(0, navigationAnchor).lastIndexOf('}');
  assert.ok(precedingEntry >= 0 && /^\s*$/u.test(originalNavigation.slice(precedingEntry + 1, navigationAnchor)),
    'application navigation catalog closing entry changed');
  const target = JSON.parse(readFileSync(moduleTarget, 'utf8'));
  assert.equal(Object.hasOwn(target, 'clientRoute'), false, 'module target already contains a client route');
  for (const [, destination] of sources) mkdirSync(join(appRoot, destination, '..'), { recursive: true });
  for (const [source, destination] of sources) copyFileSync(join(appRoot, source), join(appRoot, destination));
  writeFileSync(clientIndex, originalIndex + (originalIndex.endsWith('\n') ? '\n' : '\n\n') + operationExport + '\n' + modelExport);
  writeFileSync(navigationCatalog, originalNavigation.slice(0, precedingEntry) + '},'
    + originalNavigation.slice(precedingEntry + 1, navigationAnchor) + navigationEntry
    + originalNavigation.slice(navigationAnchor));
  writeFileSync(vueTarget, JSON.stringify({ ...target, clientRoute: {
    routePath: '/catalog/products', vueRouteName: navigationKey,
    vueComponentPath: 'ui/admin/src/views/CatalogProductsView.vue',
  } }, null, 2));
  return { clientFiles: 4, vueFiles: 3, routePath: '/catalog/products', navigationRegistered: true };
}

// 应用拥有已采纳的页面；重跑生成器只能更新自己持有的源文件。
export function verifyApplicationCrudVueRegeneration(appRoot, {
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-vue'), run = spawnSync,
} = {}) {
  mkdirSync(reportDirectory, { recursive: true });
  const adoptedPath = 'ui/admin/src/views/CatalogProductsView.vue';
  const generatedPath = 'clients/vue/productsView.vue';
  const originalAdopted = readFileSync(join(appRoot, adoptedPath), 'utf8');
  const scriptAnchor = '<script setup lang="ts">';
  assert.ok(originalAdopted.includes(scriptAnchor), 'adopted Vue page has no safe manual extension point');
  writeFileSync(join(appRoot, adoptedPath), originalAdopted.replace(scriptAnchor,
    scriptAnchor + '\n// 人工业务扩展，重新生成时必须保留。'));
  const protectedPaths = [...CRUD_ARTIFACTS, '.fullnet/codegeneration-manifest.json', adoptedPath,
    'ui/admin/src/views/products-page.generated.ts', 'ui/admin/src/views/products.generated.ts',
    'packages/client-contracts/src/index.ts', navigationCatalogPath, 'ui/admin/src/router/index.ts'];
  const capture = () => new Map(protectedPaths.map(path => [path, readFileSync(join(appRoot, path))]));
  const customized = capture();
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const args = ['exec', cli, '--schema', join(appRoot, 'verification/CrudGeneration/schema.json'),
    '--workspace', appRoot, '--apply'];
  const execute = (stage, expectedStatus) => {
    const result = run('dotnet', args, { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({
      args, status: result.status, signal: result.signal, error: result.error?.message,
      stdout: result.stdout, stderr: result.stderr,
    }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, expectedStatus, stage + ' returned an unexpected status');
    return result.stdout ?? '';
  };
  const repeat = execute('regenerate', 0).split(/\r?\n/u).filter(Boolean).sort();
  assert.deepEqual(repeat, CRUD_ARTIFACTS.map(path => 'Unchanged ' + path).sort(),
    'repeat generation changed its owned source plan');
  assert.deepEqual(capture(), customized, 'repeat generation changed generated or adopted content');
  const generated = customized.get(generatedPath);
  writeFileSync(join(appRoot, generatedPath), Buffer.concat([generated,
    Buffer.from('\n<!-- 人工修改生成源，必须拒绝覆盖。 -->\n')]));
  const conflicted = capture();
  const conflict = execute('vue-conflict', 2);
  assert.ok(conflict.split(/\r?\n/u).includes('Conflict ' + generatedPath),
    'missing exact Vue source conflict');
  assert.deepEqual(capture(), conflicted, 'conflict changed generated or adopted content');
  writeFileSync(join(appRoot, generatedPath), generated);
  assert.deepEqual(capture(), customized, 'Vue source restoration changed application content');
  return { repeatUnchanged: true, manualPreserved: true, conflictRejected: true };
}

export function verifyApplicationCrudVue(appRoot, {
  reportDirectory = join(process.cwd(), '.tmp/template-real-stack/application-crud-vue'), run = spawnSync,
} = {}) {
  const adopted = prepareApplicationCrudVue(appRoot);
  mkdirSync(reportDirectory, { recursive: true });
  const router = join(appRoot, 'ui/admin/src/router/index.ts');
  const index = join(appRoot, 'packages/client-contracts/src/index.ts');
  const target = join(appRoot, 'verification/CrudGeneration/vue-target.json');
  const protectedPaths = [index, target, join(appRoot, navigationCatalogPath),
    ...generatedNames.map((name) => join(appRoot, 'verification/ClientGeneration/generated', name)),
    ...generatedNames.map((name) => join(appRoot, clientRoot, name)),
    ...[...vueSources.values()].map((path) => join(appRoot, path))];
  const snapshot = () => new Map(protectedPaths.map((path) => [path, readFileSync(path)]));
  const execute = (stage, command, args, options = {}) => {
    const processOptions = { cwd: appRoot, encoding: 'utf8', timeout: 300_000, windowsHide: true, ...options };
    const result = command === 'pnpm'
      ? runPnpm(args, processOptions, run)
      : run(command, args, { ...processOptions, shell: false });
    writeFileSync(join(reportDirectory, stage + '.json'), JSON.stringify({ status: result.status,
      error: result.error?.message, stdout: result.stdout, stderr: result.stderr }, null, 2));
    assert.equal(result.error, undefined, stage + ' process failed');
    assert.equal(result.status, 0, stage + ' failed: ' + (result.stderr ?? result.stdout ?? ''));
    return result.stdout ?? '';
  };
  const cli = join(appRoot, 'framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
  const routeArgs = ['exec', cli, 'apply-client-route-integration', '--schema',
    join(appRoot, 'verification/CrudGeneration/schema.json'), '--repository', appRoot, '--target', target];
  assert.match(execute('route', 'dotnet', routeArgs), /Update ui\/admin\/src\/router\/index\.ts/u);
  const routed = readFileSync(router);
  assert.match(routed.toString('utf8'), /name: 'm7-catalog-products'/u);
  assert.match(routed.toString('utf8'), /path: '\/catalog\/products'/u);
  assert.match(execute('route-repeat', 'dotnet', routeArgs), /Unchanged ui\/admin\/src\/router\/index\.ts/u);
  assert.deepEqual(readFileSync(router), routed, 'repeat route integration changed the router');
  const contributor = readFileSync(join(appRoot, 'src/Demo.Modules.Catalog/CatalogAuthorizationContributor.cs'), 'utf8');
  assert.match(contributor, /new NavigationDefinition\(\s*"m7-catalog-products",\s*null,\s*"m7-catalog-products",\s*"\/catalog\/products",\s*"m7-catalog-products"/u,
    'generated server navigation must match the Vue route and local catalog');
  const regeneration = verifyApplicationCrudVueRegeneration(appRoot, { reportDirectory, run });
  const installed = snapshot();
  execute('install', 'pnpm', ['install', '--frozen-lockfile']);
  execute('navigation-build', 'pnpm', ['--filter', '@fullnet/client-contracts', 'build']);
  const navigationProbe = `import { createAdminNavigationCatalog } from './packages/client-contracts/dist/navigation-catalog.js';
const catalog = createAdminNavigationCatalog();
const node = { componentKey: 'm7-catalog-products', routeName: 'm7-catalog-products', path: '/catalog/products', children: [] };
if (!catalog.isSupportedNavigationTree([node])) process.exit(1);
if (catalog.isSupportedNavigationTree([{ ...node, routeName: 'other' }])) process.exit(2);`;
  execute('navigation-contract', 'node', ['--input-type=module', '-e', navigationProbe]);
  execute('build', 'pnpm', ['--filter', '@fullnet/admin', 'build']);
  assert.deepEqual(snapshot(), installed, 'Vue build changed adopted application sources');
  assert.deepEqual(readFileSync(router), routed, 'Vue build changed the route');
  const result = { ...adopted, routeIntegrated: true, repeatUnchanged: true, regeneration,
    vueBuilt: true, sourcesPreserved: true };
  writeFileSync(join(reportDirectory, 'result.json'), JSON.stringify(result, null, 2));
  return result;
}
