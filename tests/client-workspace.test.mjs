import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const [
  workspace,
  rootPackage,
  tokens,
  notices,
  localeCatalog,
  glossary,
  uniappPackage,
  uniappE2ePackage,
  clientCi
] = await Promise.all([
  readFile('pnpm-workspace.yaml', 'utf8'),
  readFile('package.json', 'utf8'),
  readFile('packages/design-tokens/src/tokens.css', 'utf8'),
  readFile('THIRD-PARTY-NOTICES', 'utf8'),
  readFile('localization/locales.json', 'utf8'),
  readFile('localization/glossary.json', 'utf8'),
  readFile('clients/uniapp/package.json', 'utf8'),
  readFile('tests/e2e/uniapp-h5/package.json', 'utf8'),
  readFile('.github/workflows/ci.yml', 'utf8')
]);

assert.match(workspace, /packages\/\*/);
assert.match(workspace, /ui\/\*/);
assert.match(workspace, /clients\/\*/);

const packageDefinition = JSON.parse(rootPackage);
assert.equal(packageDefinition.private, true);
assert.match(packageDefinition.packageManager, /^pnpm@10\./);
assert.equal(packageDefinition.engines.node, '>=24 <25');
assert.equal(
  packageDefinition.scripts['test:localization'],
  'node --test tests/localization-contract.test.mjs'
);
assert.equal(
  packageDefinition.scripts['test:clients'],
  'pnpm --recursive --filter=!@fullnet/admin-parity-e2e --filter=!@fullnet/admin-real-stack-e2e --filter=!@fullnet/uniapp-h5-e2e --filter=!@fullnet/admin-layui --if-present test'
);
assert.equal(
  packageDefinition.scripts['build:clients'],
  'pnpm --recursive --filter=!@fullnet/admin-layui --if-present build'
);
assert.equal(
  packageDefinition.scripts['test:e2e:admin'],
  'pnpm --filter @fullnet/admin-parity-e2e test'
);
assert.equal(
  packageDefinition.scripts['test:e2e:layui-frozen'],
  'pnpm --filter @fullnet/admin-parity-e2e test:layui-frozen'
);
assert.equal(
  packageDefinition.scripts['test:e2e'],
  'pnpm test:e2e:admin'
);
assert.equal(
  packageDefinition.scripts['test:e2e:uniapp'],
  'pnpm --filter @fullnet/uniapp-h5-e2e test'
);
assert.equal(
  packageDefinition.scripts['audit:clients'],
  'node scripts/audit-client-dependencies.mjs'
);
// DCloud 的 H5 构建从公共 hoist 解析编译器，因此根工作区必须钉住与 Vue I18n 同版的实现。
assert.equal(
  packageDefinition.pnpm.overrides['@intlify/message-compiler'],
  '9.14.5',
  'vue-i18n 与 DCloud 公共依赖提升必须统一使用已验证的消息编译器版本'
);
assert.deepEqual(packageDefinition.pnpm.overrides, {
  glob: '10.5.0',
  '@intlify/message-compiler': '9.14.5',
  '@dcloudio/uni-cli-shared>@intlify/core-base': '9.1.11',
  '@intlify/message-resolver@9.1.9': '9.1.11',
  '@intlify/message-resolver@9.1.10': '9.1.11',
  '@jimp/jpeg>jpeg-js': '0.4.4',
  '@dcloudio/uni-mp-weixin>ws': '8.21.0',
  '@dcloudio/uni-cli-shared>adm-zip': '0.6.0',
  '@dcloudio/uni-nvue-styler>postcss': '8.5.19',
  'express@4.20.0>path-to-regexp': '0.1.13',
  undici: '7.29.0',
  'brace-expansion': '2.1.4'
});
assert.deepEqual(packageDefinition.pnpm.peerDependencyRules, {
  allowedVersions: {
    '@dcloudio/vite-plugin-uni>vite': '5.4.21'
  }
});

const uniappDefinition = JSON.parse(uniappPackage);
assert.equal(uniappDefinition.name, '@fullnet/uniapp');
assert.equal(uniappDefinition.private, true);
assert.equal(uniappDefinition.type, 'module');
assert.equal(uniappDefinition.scripts.test, 'vitest run');
assert.equal(uniappDefinition.scripts.typecheck, 'vue-tsc --noEmit -p tsconfig.json');
assert.equal(uniappDefinition.scripts['build:h5'], 'uni build -p h5');
assert.equal(uniappDefinition.scripts['build:mp-weixin'], 'uni build -p mp-weixin');
assert.equal(uniappDefinition.scripts['build:mp-alipay'], 'uni build -p mp-alipay');
assert.equal(uniappDefinition.dependencies['vue-i18n'], '9.14.5');
assert.equal(uniappDefinition.devDependencies.vite, '5.4.21');
assert.equal(uniappDefinition.devDependencies.vitest, '3.2.6');

const uniappE2eDefinition = JSON.parse(uniappE2ePackage);
assert.equal(uniappE2eDefinition.name, '@fullnet/uniapp-h5-e2e');
assert.equal(uniappE2eDefinition.private, true);
assert.equal(uniappE2eDefinition.type, 'module');
assert.equal(uniappE2eDefinition.scripts.test, 'playwright test');
assert.equal(
  uniappE2eDefinition.scripts.pretest,
  'pnpm --filter @fullnet/uniapp build:h5',
  'H5 E2E 必须先重建生产产物，避免 DEV bridge 扫描误用陈旧构建'
);
assert.equal(uniappE2eDefinition.devDependencies['@playwright/test'], '1.61.1');

for (const command of [
  'pnpm --filter @fullnet/uniapp test',
  'pnpm --filter @fullnet/uniapp typecheck',
  'pnpm --filter @fullnet/uniapp build:h5',
  'pnpm --filter @fullnet/uniapp build:mp-weixin',
  'pnpm --filter @fullnet/uniapp build:mp-alipay',
  'pnpm test:e2e:uniapp'
]) {
  assert.match(clientCi, new RegExp(command.replaceAll(/[.*+?^${}()|[\]\\]/g, '\\$&')));
}
assert.match(clientCi, /tests\/e2e\/uniapp-h5\/playwright-report/);
assert.match(clientCi, /pnpm audit:clients/);
assert.doesNotMatch(clientCi, /@fullnet\/admin-layui/);
assert.match(clientCi, /pnpm test:e2e:admin/);
assert.doesNotMatch(clientCi, /admin-layui exec vite/);
assert.match(clientCi, /pnpm test:governance/);

const catalog = JSON.parse(localeCatalog);
assert.equal(catalog.defaultLocale, 'zh-CN');
assert.deepEqual(
  catalog.supportedLocales.map(locale => locale.tag),
  ['zh-CN', 'en-US']
);
assert.ok(
  catalog.supportedLocales.every(locale =>
    ['dotnet', 'web', 'uniapp', 'flutter'].every(
      platform => locale.platformMappings[platform]
    )
  )
);

const glossaryDefinition = JSON.parse(glossary);
assert.ok(Array.isArray(glossaryDefinition.terms));
assert.ok(glossaryDefinition.terms.length > 0);

assert.match(tokens, /--fullnet-color-accent:/);
assert.match(tokens, /--fullnet-shell-sidebar-width:/);
assert.match(tokens, /--fullnet-font-sans:/);

assert.match(notices, /Layui/i);
assert.match(notices, /MIT/i);
assert.match(notices, /uni-app\/DCloud/i);
assert.match(notices, /Apache-2\.0/i);
assert.match(notices, /Vue I18n/i);
