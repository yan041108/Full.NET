import { mkdir, readFile, writeFile, copyFile, symlink, readdir, lstat } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import os from 'node:os';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { generateFullNetClient } from '../../../scripts/openapi/generate-fullnet-client.mjs';

// 只生成此夹具的应用文件；复用工作区依赖，不复制仓库、依赖或后端二进制。
const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../../..');
const root = path.resolve(process.argv[2] ?? '');
const tree = process.argv[3] === '--tree';
const aggregate = process.argv[3] === '--aggregate';
const association = process.argv[3] === '--association';
if (process.argv[3] && !tree && !aggregate && !association) throw new Error('未知生成夹具场景。');
if (!process.argv[2] || !root.toLowerCase().startsWith(path.resolve(os.tmpdir()).toLowerCase() + path.sep)
  || root === repository || root.startsWith(repository + path.sep)) throw new Error('须指定仓库外的系统临时子目录。');
await mkdir(root, { recursive: true });
const marker = '.fullnet-generated-workbench-fixture';
if ((await lstat(root)).isSymbolicLink()) throw new Error('夹具根目录不得是链接。');
if ((await readdir(root)).length > 0 && (await readFile(path.join(root, marker), 'utf8').catch(() => '')) !== 'generated-workbench-v1')
  throw new Error('目录含有不属于此夹具的内容。');
await writeFile(path.join(root, marker), 'generated-workbench-v1');
const column = (name, scalarType, rest = {}) => ({ databaseName: name, clrPropertyName: name,
  jsonPropertyName: name[0].toLowerCase() + name.slice(1), scalarType, ...rest });
const schema = { ownerKey: 'acme', moduleKey: 'catalog', entityKey: 'workbench', databaseTableName: 'acme_catalog_workbench',
  rootNamespace: 'Acme.Remediation', clrTypeName: 'Workbench', apiResourceName: 'workbenches', permissionResourceName: 'workbenches',
  dataScope: 'tenant.required', entityCapabilities: { deleteMode: 'soft.delete', hasCreatedAudit: false, hasUpdatedAudit: false,
    hasDeletedAudit: true, hasVersion: true, ownershipMode: 'none' },
  columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('Name', 'String', { maxLength: 64 }),
    column('Quantity', 'Int64'), column('Price', 'Decimal', { numericPrecision: 18, numericScale: 4,
      ui: { controlKind: 'number', showInList: true, includeInCreate: true, includeInUpdate: true, required: true,
        sortable: true, queryable: true, queryKind: 'range', unique: false, includeInImportExport: false } }),
    column('IsReady', 'Boolean', { isNullable: true }), column('ReferenceId', 'Uuid', { isNullable: true }),
    column('OccurredAtUtc', 'DateTimeUtc', { isNullable: true }), column('Version', 'Int64'), column('IsDeleted', 'Boolean'),
    column('DeletedAtUtc', 'DateTimeUtc', { isNullable: true }), column('DeletedById', 'Uuid', { isNullable: true })] };
if (tree) Object.assign(schema, { entityKey: 'tree_node', databaseTableName: 'acme_catalog_tree_node', clrTypeName: 'TreeNode',
  apiResourceName: 'tree-nodes', permissionResourceName: 'tree_nodes', scene: 'tree',
  columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('Name', 'String', { maxLength: 64 }),
    column('ParentId', 'Uuid', { isNullable: true }), column('Version', 'Int64'), column('IsDeleted', 'Boolean'),
    column('DeletedAtUtc', 'DateTimeUtc', { isNullable: true }), column('DeletedById', 'Uuid', { isNullable: true })] });
if (aggregate) {
  const capabilities = { ...schema.entityCapabilities };
  const system = [column('Version', 'Int64'), column('IsDeleted', 'Boolean'), column('DeletedAtUtc', 'DateTimeUtc', { isNullable: true }), column('DeletedById', 'Uuid', { isNullable: true })];
  Object.assign(schema, { entityKey: 'sales_order', databaseTableName: 'acme_catalog_sales_order', clrTypeName: 'SalesOrder',
    apiResourceName: 'sales-orders', permissionResourceName: 'sales_orders', scene: 'master.detail',
    columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('Name', 'String', { maxLength: 64 }), ...system],
    relationships: [{ principalEntityKey: 'sales_order', principalColumnName: 'Id', principalDataScope: 'tenant.required',
      dependentEntityKey: 'sales_line', dependentColumnName: 'OrderId', dependentDataScope: 'tenant.required',
      compositeKeyColumnNames: ['OrderId', 'Id'], cascadeDelete: true,
      dependentSchema: { ownerKey: 'acme', moduleKey: 'catalog', entityKey: 'sales_line', databaseTableName: 'acme_catalog_sales_line',
        rootNamespace: 'Acme.Remediation', clrTypeName: 'SalesLine', apiResourceName: 'sales-lines', permissionResourceName: 'sales_lines',
        dataScope: 'tenant.required', entityCapabilities: capabilities,
        columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('OrderId', 'Uuid', { clrPropertyName: 'OrderReferenceId', jsonPropertyName: 'orderReferenceId' }),
          column('Description', 'String', { maxLength: 64 }), column('Quantity', 'Int64'), ...system] } }] });
}
const schemaPath = path.join(root, 'schema.json');
if (association) {
  const system = schema.columns.filter(column => ['Version', 'IsDeleted', 'DeletedAtUtc', 'DeletedById'].includes(column.databaseName));
  const principal = (key, type) => ({ ownerKey: 'acme', moduleKey: 'catalog', entityKey: key, databaseTableName: 'acme_catalog_' + key,
    rootNamespace: 'Acme.Remediation', clrTypeName: type, apiResourceName: key + 's', permissionResourceName: key + 's', dataScope: 'tenant.required',
    entityCapabilities: { ...schema.entityCapabilities }, columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('Name', 'String', { maxLength: 64 }), ...system] });
  Object.assign(schema, { entityKey: 'item_label', databaseTableName: 'acme_catalog_item_label', clrTypeName: 'ItemLabel', apiResourceName: 'item-labels', permissionResourceName: 'item_labels', scene: 'many.to.many',
    columns: [column('Id', 'Uuid'), column('TenantId', 'Uuid'), column('ItemId', 'Uuid', { clrPropertyName: 'ItemReferenceId', jsonPropertyName: 'itemReferenceId' }), column('LabelId', 'Uuid'), ...system],
    relationships: [{ principalEntityKey: 'item', principalColumnName: 'Id', principalDataScope: 'tenant.required', dependentEntityKey: 'item_label', dependentColumnName: 'ItemId', dependentDataScope: 'tenant.required', compositeKeyColumnNames: ['ItemId', 'LabelId'], cascadeDelete: false, principalSchema: principal('item', 'Item') },
      { principalEntityKey: 'label', principalColumnName: 'Id', principalDataScope: 'tenant.required', dependentEntityKey: 'item_label', dependentColumnName: 'LabelId', dependentDataScope: 'tenant.required', compositeKeyColumnNames: ['ItemId', 'LabelId'], cascadeDelete: false, principalSchema: principal('label', 'Label') }] });
}
await writeFile(schemaPath, JSON.stringify(schema));
const cli = path.join(repository, 'src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll');
const result = spawnSync('dotnet', [cli, '--schema', schemaPath, '--workspace', root, '--apply'],
  { encoding: 'utf8', windowsHide: true, timeout: 30_000 });
if (result.status !== 0) throw new Error(result.stderr + result.stdout);
const resource = schema.apiResourceName;
const generated = path.join(root, 'generated/acme/catalog', schema.entityKey);
const directories = ['src/views', 'src/api', 'src/auth', 'src/i18n', 'src/client'];
await Promise.all(directories.map(directory => mkdir(path.join(root, directory), { recursive: true })));
for (const file of [`${resource}.generated.ts`, `${resource}-page.generated.ts`, `${resource}View.vue`])
  await copyFile(path.join(generated, 'clients/vue', file), path.join(root, 'src/views', file));
await generateFullNetClient({ inputPath: path.join(generated, `contracts/openapi/${resource}.generated.openapi.json`),
  outputDirectory: path.join(root, 'src/client'), httpModuleSpecifier: '@base-client/http' });
const source = file => path.join(repository, file).replaceAll('\\', '/');
const quoted = file => JSON.stringify(source(file));
await writeFile(path.join(root, 'src/client/index.ts'), `export * from './index.generated';
export * from '@base-client/http';
export { isFullNetProblemDetails } from '@base-client/problem-details';
export type { FullNetProblemDetails } from '@base-client/problem-details';\n`);
await writeFile(path.join(root, 'src/api/http.ts'), `import { createHttpClient } from '@base-client/http'; export const http = createHttpClient();\n`);
await writeFile(path.join(root, 'src/auth/session.ts'), `export const useSessionStore = () => ({ can: (_permission: string) => true });\n`);
await writeFile(path.join(root, 'src/i18n/adminI18n.ts'), `export * from ${quoted('ui/admin/src/i18n/adminI18n.ts')};\n`);
await writeFile(path.join(root, 'src/main.ts'), `import { createApp } from 'vue'; import App from './App.vue'; import 'element-plus/dist/index.css';
import ${quoted('packages/design-tokens/src/tokens.css')}; import ${quoted('ui/admin/src/framework/art-design/theme/art-layout.css')};
import ${quoted('ui/admin/src/framework/art-design/theme/art-theme.css')};
createApp(App).mount('#app');\n`);
await writeFile(path.join(root, 'src/App.vue'), `<script setup lang="ts">
import { computed } from 'vue'; import { ElConfigProvider } from 'element-plus';
import zh from 'element-plus/es/locale/lang/zh-cn'; import en from 'element-plus/es/locale/lang/en';
import Workbench from './views/${resource}View.vue'; import { useAdminI18n } from './i18n/adminI18n';
const { locale, setLocale } = useAdminI18n(); const elementLocale = computed(() => locale.value === 'en-US' ? en : zh);
</script><template><el-config-provider :locale="elementLocale"><main class="art-admin-shell"><h1>Generated CRUD</h1>
<nav aria-label="Language"><button @click="setLocale('zh-CN')">中文</button><button @click="setLocale('en-US')">English</button></nav>
<Workbench /></main></el-config-provider></template><style>body { margin: 16px; font: 16px system-ui; } * { box-sizing: border-box; }
main.art-admin-shell { display: block; min-width: 0; height: auto; min-height: 0; overflow: visible; }</style>`);
await writeFile(path.join(root, 'index.html'), '<!doctype html><html lang="zh-CN"><head><title>Generated CRUD</title></head><body><div id="app"></div><script type="module" src="/src/main.ts"></script></body></html>');
await writeFile(path.join(root, 'package.json'), '{"type":"module"}');
try { await symlink(source('ui/admin/node_modules'), path.join(root, 'node_modules'), 'junction'); }
catch (error) { if (error.code !== 'EEXIST') throw error; }
const aliases = { '@fullnet/client-contracts': path.join(root, 'src/client/index.ts').replaceAll('\\', '/'),
  '@base-client': source('packages/client-contracts/src'), '@fullnet/admin-i18n': source('packages/admin-i18n/src/index.ts') };
await writeFile(path.join(root, 'vite.config.mjs'), `import { defineConfig } from 'vite'; import vue from '@vitejs/plugin-vue';
export default defineConfig({ root: ${JSON.stringify(root)}, plugins: [vue()], resolve: { alias: ${JSON.stringify(aliases)}, dedupe: ['vue'] },
server: { host: '127.0.0.1', port: 15174, strictPort: true, fs: { allow: [${JSON.stringify(root)}, ${JSON.stringify(repository)}] } } });`);
const tsconfig = JSON.parse(await readFile(path.join(repository, 'ui/admin/tsconfig.json'), 'utf8'));
tsconfig.compilerOptions.paths = Object.fromEntries(Object.entries(aliases).map(([alias, target]) =>
  [alias === '@base-client' ? alias + '/*' : alias, [alias === '@base-client' ? target + '/*' : target]]));
tsconfig.include = ['src/**/*.ts', 'src/**/*.vue'];
await writeFile(path.join(root, 'tsconfig.json'), JSON.stringify(tsconfig));
const playwright = pathToFileURL(path.join(repository, 'tests/e2e/admin-parity/node_modules/@playwright/test/index.mjs')).href;
await writeFile(path.join(root, 'playwright.config.mjs'), `import { defineConfig } from ${JSON.stringify(playwright)};
export default defineConfig({ testDir: ${quoted('tests/e2e/generated-crud')}, testMatch: '${tree ? 'tree' : aggregate ? 'aggregate' : association ? 'association' : 'workbench'}.spec.mjs', timeout: 60000,
workers: 1, reporter: 'line', use: { channel: 'msedge', baseURL: 'http://127.0.0.1:15174', timezoneId: 'Asia/Shanghai', trace: 'retain-on-failure' },
webServer: { command: 'node node_modules/vite/bin/vite.js --config vite.config.mjs', cwd: ${JSON.stringify(root)}, url: 'http://127.0.0.1:15174', reuseExistingServer: false } });`);
process.stdout.write(root + '\n');
