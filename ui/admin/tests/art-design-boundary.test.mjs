import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import test from 'node:test';

const repoRoot = path.resolve(fileURLToPath(new URL('../../..', import.meta.url)));
const upstreamDocPath = path.join(repoRoot, 'docs/upstreams/art-design-pro.md');
const packageJsonPath = path.join(repoRoot, 'ui/admin/package.json');

const forbiddenDependencies = [
  'axios',
  'pinia-plugin-persistedstate',
  '@wangeditor/editor',
  'xlsx',
  'xgplayer',
  'crypto-js'
];

test('Art Design Pro 上游来源清单存在且锁定提交', () => {
  const content = readFileSync(upstreamDocPath, 'utf8');
  assert.match(content, /f3aaf58eec1a0e988f162352c33862327a484f95/);
  assert.match(content, /MIT/i);
  assert.match(content, /ui\/admin\/src\/framework\/art-design/);
});

test('Vue 管理端默认依赖不包含 Art Design Pro 禁止项', () => {
  const packageJson = JSON.parse(readFileSync(packageJsonPath, 'utf8'));
  const dependencies = {
    ...packageJson.dependencies,
    ...packageJson.devDependencies
  };

  assert.equal(dependencies.echarts, '6.1.0');

  for (const dependency of forbiddenDependencies) {
    assert.equal(
      dependencies[dependency],
      undefined,
      `禁止依赖 ${dependency} 不得进入 ui/admin/package.json`
    );
  }
});
