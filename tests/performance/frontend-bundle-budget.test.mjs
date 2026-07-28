import assert from 'node:assert/strict';
import { mkdtemp, mkdir, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';
import * as bundleBudget
  from '../../scripts/testing/check-frontend-bundle-budget.mjs';

const {
  evaluateBudget,
  measureStaticJavaScriptGraph
} = bundleBudget;

test('静态依赖图计入同步 import 并排除动态 import', async () => {
  const root = await mkdtemp(path.join(tmpdir(), 'fullnet-bundle-budget-'));
  try {
    const assets = path.join(root, 'assets');
    await mkdir(assets);
    await writeFile(
      path.join(assets, 'index-entry.js'),
      'import "./shared.js"; const load = () => import("./lazy.js");'
    );
    await writeFile(
      path.join(assets, 'shared.js'),
      'import "./nested.js"; export const shared = true;'
    );
    await writeFile(path.join(assets, 'nested.js'), 'export const nested = true;');
    await writeFile(path.join(assets, 'lazy.js'), 'export const lazy = true;');

    const result = await measureStaticJavaScriptGraph(root, {
      directory: 'assets',
      entryPrefix: 'index-',
      entrySuffix: '.js',
      followStaticImports: true
    });

    assert.deepEqual(
      result.files.map(file => path.basename(file)).sort(),
      ['index-entry.js', 'nested.js', 'shared.js']
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test('预算只允许配置比例内的相对退化', () => {
  assert.doesNotThrow(() => evaluateBudget(
    'Vue initial JS',
    { minifiedBytes: 1049, gzipBytes: 524 },
    {
      minifiedBytes: 1000,
      gzipBytes: 500,
      maxRegressionPercent: 5
    }
  ));

  assert.throws(
    () => evaluateBudget(
      'Vue initial JS',
      { minifiedBytes: 1051, gzipBytes: 500 },
      {
        minifiedBytes: 1000,
        gzipBytes: 500,
        maxRegressionPercent: 5
      }
    ),
    /Vue initial JS.*minified/
  );
});

test('延迟加载资产可以按稳定前缀纳入独立预算', async () => {
  const root = await mkdtemp(path.join(tmpdir(), 'fullnet-lazy-budget-'));
  try {
    const assets = path.join(root, 'assets');
    await mkdir(assets);
    await writeFile(path.join(assets, 'FullNetChart-hash.js'), 'export const chart = true;');
    await writeFile(path.join(assets, 'index-entry.js'), 'export const entry = true;');

    const result = await bundleBudget.measureJavaScriptAsset(root, {
      directory: 'assets',
      assetPrefix: 'FullNetChart-',
      assetSuffix: '.js'
    });

    assert.deepEqual(
      result.files.map(file => path.basename(file)),
      ['FullNetChart-hash.js']
    );
    assert.equal(result.minifiedBytes, 26);
    assert.ok(result.gzipBytes > 0);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});
