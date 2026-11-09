#!/usr/bin/env node
/**
 * 将应用模板与固定提交的框架源码组装为可安装的 dotnet new 模板目录。
 */
import { cpSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { spawnSync } from 'node:child_process';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { assertArchiveEntryModes, buildSourceBundle, parseBundleArguments, TEMPLATE_INPUT_ROOTS } from './build-source-bundle.mjs';
import { copyApplicationClientTools } from './application-client-tools.mjs';
import { createApplicationPackage } from './application-package.mjs';
import { writeDistributionManifest } from './distribution-manifest.mjs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(SCRIPT_DIR, '..', '..');
const DEFAULT_OUTPUT = join(REPO_ROOT, 'artifacts', 'templates', 'fullnet-app-package');

function extractTemplateInputs(sourceCommit, run) {
  const root = mkdtempSync(join(tmpdir(), 'fullnet-template-inputs-'));
  try {
    const tree = run('git', ['ls-tree', '-r', '-z', sourceCommit, '--', ...TEMPLATE_INPUT_ROOTS], {
      cwd: REPO_ROOT, encoding: 'utf8', timeout: 30_000, windowsHide: true,
    });
    if (tree.status !== 0) throw new Error('Cannot inspect frozen template inputs: ' + (tree.stderr || tree.error || 'git'));
    assertArchiveEntryModes(tree.stdout);
    const archive = join(root, 'inputs.tar');
    for (const [command, args] of [['git', ['archive', '--format=tar', '--output', archive, sourceCommit, ...TEMPLATE_INPUT_ROOTS]],
      ['tar', ['-xf', archive, '-C', root]]]) {
      const result = run(command, args, { cwd: REPO_ROOT, encoding: 'utf8', timeout: 60_000, windowsHide: true });
      if (result.error) throw result.error;
      if (result.status !== 0) throw new Error('Cannot extract frozen template inputs: ' + (result.stderr || result.stdout || command));
    }
    return root;
  } catch (error) { rmSync(root, { recursive: true, force: true }); throw error; }
}

export function buildAppTemplate({ output = DEFAULT_OUTPUT, sourceCommit, run = spawnSync } = {}) {
  const templateRoot = resolve(output);
  // 分发包只允许写入空目录，避免模板构建覆盖应用或其他人工文件。
  if (existsSync(templateRoot) && readdirSync(templateRoot).length > 0) {
    throw new Error('Template output directory must be empty: ' + templateRoot);
  }
  const { bundleRoot, manifest } = buildSourceBundle({ output: join(templateRoot, 'framework', 'fullnet'), sourceCommit, run });
  const frozenInputs = extractTemplateInputs(manifest.sourceCommit, run);
  try {
    // 模板和创建工具也来自同一 Git 提交，不能把工作树中的新文件混入旧提交分发包。
    cpSync(join(frozenInputs, 'templates', 'fullnet-app'), templateRoot, { recursive: true, force: false });
    copyFileSync(join(bundleRoot, 'framework-manifest.json'), join(templateRoot, 'framework-manifest.json'));
    copyFileSync(join(bundleRoot, 'global.json'), join(templateRoot, 'global.json'));
    // 管理端是应用拥有的骨架；共享源码与锁文件随分发包固定版本交付。
    for (const relative of [
      'ui/admin',
      'packages/client-contracts',
      'packages/admin-i18n',
      'packages/admin-form-designer',
      'packages/design-tokens',
      'packages/braces',
      'patches',
    ]) {
      cpSync(join(bundleRoot, relative), join(templateRoot, relative), { recursive: true, force: false });
    }
    for (const relative of ['LICENSE', 'THIRD-PARTY-NOTICES', 'package.json', 'pnpm-lock.yaml', 'pnpm-workspace.yaml', 'contracts/workflow/gateway-limits.json', 'contracts/workflow/vform3-runtime-release.json']) {
      mkdirSync(dirname(join(templateRoot, relative)), { recursive: true });
      copyFileSync(join(bundleRoot, relative), join(templateRoot, relative));
    }
    writeFileSync(join(templateRoot, 'package.json'), JSON.stringify(createApplicationPackage(
      JSON.parse(readFileSync(join(bundleRoot, 'package.json'), 'utf8')),
      JSON.parse(readFileSync(join(bundleRoot, 'templates/fullnet-app/package.json'), 'utf8'))), null, 2) + '\n');

    const configTemplate = join(templateRoot, 'appsettings.json.template');
    copyFileSync(configTemplate, join(templateRoot, 'appsettings.json'));
    copyFileSync(configTemplate, join(templateRoot, 'src', 'FullNetAppNameToken.Host.Api', 'appsettings.json'));
    copyFileSync(configTemplate, join(templateRoot, 'src', 'FullNetAppNameToken.Host.Migrator', 'appsettings.json'));
    const workerConfigPath = join(templateRoot, 'src', 'FullNetAppNameToken.Host.Worker', 'appsettings.json');
    // Worker 健康端点使用独立端口，避免本地 API 与 Worker 同时启动时发生绑定冲突。
    writeFileSync(workerConfigPath,
      readFileSync(configTemplate, 'utf8').replace('http://localhost:5180', 'http://localhost:5181'));
    rmSync(configTemplate);
    const toolRoot = join(templateRoot, '.fullnet-tools');
    mkdirSync(toolRoot);
    for (const tool of ['create-app.mjs', 'diagnose-app.mjs', 'application-package.mjs', 'distribution-manifest.mjs', 'framework-manifest-utils.mjs', 'migration-script-modules.mjs', 'preset-modules.mjs', 'project-preset-composition.mjs', 'verify-created-app.mjs', 'upgrade-framework.mjs', 'framework-upgrade-integrity.mjs', 'framework-upgrade-store.mjs']) {
      copyFileSync(join(frozenInputs, 'scripts', 'templates', tool), join(toolRoot, tool));
    }
    copyApplicationClientTools(bundleRoot, templateRoot);
    return { templateRoot, distributionDigest: writeDistributionManifest(templateRoot) };
  } finally { rmSync(frozenInputs, { recursive: true, force: true }); }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2);
    if (args.length === 1 && ['--help', '-h'].includes(args[0])) {
      console.log('Usage: node scripts/templates/build-app-template.mjs [--output dir] [--source-commit full-sha]');
      process.exit(0);
    }
    const { templateRoot, distributionDigest } = buildAppTemplate(parseBundleArguments(args, DEFAULT_OUTPUT));
    console.log('Wrote application template to ' + templateRoot);
    console.log('Distribution manifest SHA-256: ' + distributionDigest);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}
