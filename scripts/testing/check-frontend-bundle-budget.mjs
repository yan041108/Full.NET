import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { gzipSync } from 'node:zlib';

const staticImportPatterns = [
  /\bimport\s*["']([^"']+)["']/g,
  /\bimport(?!\s*\()[^;]*?\bfrom\s*["']([^"']+)["']/g
];

export async function measureJavaScriptAsset(distRoot, definition) {
  const assetRoot = path.resolve(distRoot, definition.directory);
  const candidates = (await readdir(assetRoot))
    .filter(name =>
      name.startsWith(definition.assetPrefix)
      && name.endsWith(definition.assetSuffix));
  if (candidates.length !== 1) {
    throw new Error(
      `Expected one asset matching ${definition.assetPrefix}*`
      + `${definition.assetSuffix} in ${assetRoot}, found ${candidates.length}.`
    );
  }

  const file = path.join(assetRoot, candidates[0]);
  const content = await readFile(file);
  return {
    files: [file],
    minifiedBytes: content.byteLength,
    gzipBytes: gzipSync(content).byteLength
  };
}

export async function measureStaticJavaScriptGraph(distRoot, definition) {
  const assetRoot = path.resolve(distRoot, definition.directory);
  const candidates = (await readdir(assetRoot))
    .filter(name =>
      name.startsWith(definition.entryPrefix)
      && name.endsWith(definition.entrySuffix));
  if (candidates.length !== 1) {
    throw new Error(
      `Expected one entry matching ${definition.entryPrefix}*`
      + `${definition.entrySuffix} in ${assetRoot}, found ${candidates.length}.`
    );
  }

  const queue = [path.join(assetRoot, candidates[0])];
  const files = [];
  const visited = new Set();
  let minifiedBytes = 0;
  let gzipBytes = 0;

  while (queue.length > 0) {
    const file = path.resolve(queue.shift());
    if (visited.has(file)) {
      continue;
    }

    ensureWithinRoot(assetRoot, file);
    visited.add(file);
    files.push(file);
    const content = await readFile(file);
    minifiedBytes += content.byteLength;
    gzipBytes += gzipSync(content).byteLength;

    if (!definition.followStaticImports) {
      continue;
    }

    const source = content.toString('utf8');
    for (const pattern of staticImportPatterns) {
      pattern.lastIndex = 0;
      for (const match of source.matchAll(pattern)) {
        if (!match[1].endsWith('.js')) {
          continue;
        }

        const dependency = path.resolve(path.dirname(file), match[1]);
        ensureWithinRoot(assetRoot, dependency);
        queue.push(dependency);
      }
    }
  }

  return { files, minifiedBytes, gzipBytes };
}

export function evaluateBudget(name, actual, budget) {
  const metrics = [
    ['minified', actual.minifiedBytes, budget.minifiedBytes],
    ['gzip', actual.gzipBytes, budget.gzipBytes]
  ];
  for (const [metric, value, baseline] of metrics) {
    const maximum = baseline * (1 + budget.maxRegressionPercent / 100);
    if (value > maximum) {
      throw new Error(
        `${name} ${metric} size ${value} exceeds baseline ${baseline} `
        + `by more than ${budget.maxRegressionPercent}%.`
      );
    }
  }
}

async function run(configPath) {
  const absoluteConfigPath = path.resolve(configPath);
  const repositoryRoot = path.dirname(path.dirname(path.dirname(
    absoluteConfigPath
  )));
  const config = JSON.parse(await readFile(absoluteConfigPath, 'utf8'));
  for (const target of config.targets) {
    const actual = target.entry
      ? await measureStaticJavaScriptGraph(
          path.join(repositoryRoot, target.dist),
          target.entry
        )
      : await measureJavaScriptAsset(
          path.join(repositoryRoot, target.dist),
          target.asset
        );
    evaluateBudget(target.name, actual, target.budget);
    const minifiedDelta = toPercent(
      actual.minifiedBytes,
      target.budget.minifiedBytes
    );
    const gzipDelta = toPercent(
      actual.gzipBytes,
      target.budget.gzipBytes
    );
    const assetDescription = target.entry
      ? `${actual.files.length} static chunks`
      : path.basename(actual.files[0]);
    console.log(
      `PASS ${target.name}: ${assetDescription}, `
      + `minified ${actual.minifiedBytes} bytes (${minifiedDelta}), `
      + `gzip ${actual.gzipBytes} bytes (${gzipDelta})`
    );
  }
}

function ensureWithinRoot(root, candidate) {
  const relative = path.relative(root, candidate);
  if (relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error(`Bundle dependency escapes asset root: ${candidate}`);
  }
}

function toPercent(actual, baseline) {
  const percent = ((actual - baseline) / baseline) * 100;
  return `${percent >= 0 ? '+' : ''}${percent.toFixed(2)}%`;
}

if (process.argv[1]
    && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const configPath = process.argv[2]
    ?? 'tests/performance/frontend-bundle-budgets.json';
  await run(configPath);
}
