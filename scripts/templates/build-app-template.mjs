#!/usr/bin/env node
/**
 * 将应用模板与固定提交的框架源码组装为可安装的 dotnet new 模板目录。
 */
import { cpSync, copyFileSync, existsSync, mkdirSync, readdirSync, rmSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildSourceBundle } from './build-source-bundle.mjs';

const SCRIPT_DIR = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(SCRIPT_DIR, '..', '..');
const TEMPLATE_ROOT = join(REPO_ROOT, 'templates', 'fullnet-app');
const DEFAULT_OUTPUT = join(REPO_ROOT, 'artifacts', 'templates', 'fullnet-app-package');

export function buildAppTemplate({ output = DEFAULT_OUTPUT } = {}) {
  const templateRoot = resolve(output);
  // 分发包只允许写入空目录，避免模板构建覆盖应用或其他人工文件。
  if (existsSync(templateRoot) && readdirSync(templateRoot).length > 0) {
    throw new Error('Template output directory must be empty: ' + templateRoot);
  }
  mkdirSync(templateRoot, { recursive: true });
  cpSync(TEMPLATE_ROOT, templateRoot, { recursive: true, force: false });

  const { bundleRoot } = buildSourceBundle({ output: join(templateRoot, 'framework', 'fullnet') });
  copyFileSync(join(bundleRoot, 'framework-manifest.json'), join(templateRoot, 'framework-manifest.json'));
  copyFileSync(join(bundleRoot, 'global.json'), join(templateRoot, 'global.json'));

  const configTemplate = join(templateRoot, 'appsettings.json.template');
  copyFileSync(configTemplate, join(templateRoot, 'appsettings.json'));
  copyFileSync(configTemplate, join(templateRoot, 'src', 'FullNetAppNameToken.Host.Api', 'appsettings.json'));
  rmSync(configTemplate);
  const toolRoot = join(templateRoot, '.fullnet-tools');
  mkdirSync(toolRoot);
  for (const tool of ['create-app.mjs', 'preset-modules.mjs', 'project-preset-composition.mjs', 'verify-created-app.mjs']) {
    copyFileSync(join(SCRIPT_DIR, tool), join(toolRoot, tool));
  }
  return { templateRoot };
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length > 4 || (process.argv[2] && process.argv[2] !== '--output')) {
      throw new Error('Usage: node scripts/templates/build-app-template.mjs [--output dir]');
    }
    if (process.argv[2] === '--output' && !process.argv[3]) {
      throw new Error('Missing value for --output');
    }
    const { templateRoot } = buildAppTemplate({ output: process.argv[3] ?? DEFAULT_OUTPUT });
    console.log('Wrote application template to ' + templateRoot);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}
