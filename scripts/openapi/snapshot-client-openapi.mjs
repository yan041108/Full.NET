import { execFileSync } from 'node:child_process';
import {
  mkdtemp,
  mkdir,
  readFile,
  rm,
  writeFile
} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

import {
  normalizeClientOpenApi,
  serializeClientOpenApi
} from './normalize-client-openapi.mjs';
import { validateClientGenerationReadiness } from './validate-client-generation-readiness.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(scriptDirectory, '..', '..');
const manifestPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'client-generation-manifest-v1.json'
);
const snapshotPath = path.join(
  repositoryRoot,
  'contracts',
  'openapi',
  'fullnet-client-v1.openapi.json'
);
const integrationProject = path.join(
  repositoryRoot,
  'tests',
  'Full.NET.IntegrationTests',
  'Full.NET.IntegrationTests.csproj'
);
const integrationAssembly = path.join(
  repositoryRoot,
  'tests',
  'Full.NET.IntegrationTests',
  'bin',
  'Release',
  'net10.0',
  'Full.NET.IntegrationTests.dll'
);

const options = parseArguments(process.argv.slice(2));
await main(options);

async function main(currentOptions) {
  const manifest = await readManifest();
  const operationIds = manifest.entries.map(entry => entry.operationId);

  if (currentOptions.normalizeOnly) {
    const document = await readJson(currentOptions.input);
    const normalized = normalizeClientOpenApi(document, operationIds);
    await mkdir(path.dirname(currentOptions.output), { recursive: true });
    await writeFile(currentOptions.output, serializeClientOpenApi(normalized), 'utf8');
    return;
  }

  if (currentOptions.offline) {
    if (currentOptions.mode !== 'check') {
      throw new Error('--offline 只允许与 --check 组合。');
    }
    const snapshot = await readJson(snapshotPath);
    const serialized = serializeClientOpenApi(normalizeClientOpenApi(
      snapshot,
      operationIds
    ));
    await assertSnapshotMatches(serialized);
    assertReadiness(snapshot);
    process.stdout.write('客户端 OpenAPI 离线快照与 manifest 校验通过。\n');
    return;
  }

  if (!currentOptions.noBuild) {
    execFileSync('dotnet', [
      'build',
      integrationProject,
      '--configuration',
      'Release',
      '--nologo'
    ], {
      cwd: repositoryRoot,
      stdio: 'inherit'
    });
  }

  const temporaryDirectory = await mkdtemp(path.join(
    os.tmpdir(),
    'fullnet-client-openapi-'
  ));
  try {
    const providers = currentOptions.provider
      ? [currentOptions.provider]
      : ['SqlServer', 'MySql'];
    const snapshots = [];
    for (const provider of providers) {
      const rawPath = path.join(temporaryDirectory, `${provider}.openapi.json`);
      exportRuntimeDocument(provider, rawPath);
      const rawDocument = await readJson(rawPath);
      const normalized = normalizeClientOpenApi(rawDocument, operationIds);
      assertReadiness(normalized);
      snapshots.push({
        provider,
        serialized: serializeClientOpenApi(normalized)
      });
    }

    if (snapshots.length === 2 && snapshots[0].serialized !== snapshots[1].serialized) {
      throw new Error(
        `SQL Server/MySQL 客户端 OpenAPI 不一致：${firstDifference(
          snapshots[0].serialized,
          snapshots[1].serialized
        )}`
      );
    }

    const serialized = snapshots[0].serialized;
    if (currentOptions.mode === 'update') {
      await mkdir(path.dirname(snapshotPath), { recursive: true });
      await writeFile(snapshotPath, serialized, 'utf8');
      process.stdout.write(`已更新客户端 OpenAPI 快照：${snapshotPath}\n`);
      return;
    }

    await assertSnapshotMatches(serialized);
    process.stdout.write(
      `客户端 OpenAPI 运行时快照校验通过：${providers.join(', ')}。\n`
    );
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }
}

async function readManifest() {
  const manifest = await readJson(manifestPath);
  if (manifest.schemaVersion !== 1 || !Array.isArray(manifest.entries)
    || manifest.entries.length === 0) {
    throw new Error('客户端生成 manifest 结构无效。');
  }

  const operationIds = new Set();
  for (const entry of manifest.entries) {
    if (!isNonEmptyString(entry.operationId)
      || !isNonEmptyString(entry.apiModule)
      || !isNonEmptyString(entry.generatedGroup)
      || !['pilot', 'generated'].includes(entry.status)) {
      throw new Error('客户端生成 manifest 条目结构无效。');
    }
    if (operationIds.has(entry.operationId)) {
      throw new Error(`客户端生成 manifest 包含重复 operationId：${entry.operationId}`);
    }
    operationIds.add(entry.operationId);
  }
  return manifest;
}

function exportRuntimeDocument(provider, outputPath) {
  const filter = `FullyQualifiedName~OpenApiDocumentationApi${provider}Tests`;
  execFileSync('dotnet', [
    integrationAssembly,
    '--no-ansi',
    '--progress',
    'off',
    '--filter',
    filter,
    '--minimum-expected-tests',
    '1',
    '--timeout',
    '5m'
  ], {
    cwd: repositoryRoot,
    env: {
      ...process.env,
      FULLNET_CLIENT_OPENAPI_EXPORT_PATH: outputPath
    },
    stdio: 'inherit'
  });
}

function assertReadiness(document) {
  const violations = validateClientGenerationReadiness(document);
  if (violations.length > 0) {
    throw new Error(`客户端 OpenAPI 未通过生成就绪门禁：\n${violations.join('\n')}`);
  }
}

async function assertSnapshotMatches(actual) {
  let expected;
  try {
    expected = await readFile(snapshotPath, 'utf8');
  } catch (error) {
    if (error?.code === 'ENOENT') {
      throw new Error('客户端 OpenAPI 快照不存在；请显式执行 --update。');
    }
    throw error;
  }
  if (expected !== actual) {
    throw new Error(`客户端 OpenAPI 快照漂移：${firstDifference(expected, actual)}`);
  }
}

function firstDifference(left, right) {
  const leftLines = left.split('\n');
  const rightLines = right.split('\n');
  const count = Math.max(leftLines.length, rightLines.length);
  for (let index = 0; index < count; index += 1) {
    if (leftLines[index] !== rightLines[index]) {
      return `第 ${index + 1} 行，期望 ${JSON.stringify(leftLines[index])}，实际 ${JSON.stringify(rightLines[index])}`;
    }
  }
  return '字节内容不同';
}

function parseArguments(args) {
  const result = {
    mode: 'check',
    provider: null,
    offline: false,
    noBuild: false,
    normalizeOnly: false,
    input: null,
    output: null
  };
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === '--update') {
      result.mode = 'update';
    } else if (argument === '--check') {
      result.mode = 'check';
    } else if (argument === '--offline') {
      result.offline = true;
    } else if (argument === '--no-build') {
      result.noBuild = true;
    } else if (argument === '--normalize-only') {
      result.normalizeOnly = true;
    } else if (argument === '--provider') {
      result.provider = args[index + 1] ?? null;
      index += 1;
    } else if (argument === '--input') {
      result.input = path.resolve(args[index + 1] ?? '');
      index += 1;
    } else if (argument === '--output') {
      result.output = path.resolve(args[index + 1] ?? '');
      index += 1;
    } else {
      throw new Error(`未知参数：${argument}`);
    }
  }

  if (result.provider && !['SqlServer', 'MySql'].includes(result.provider)) {
    throw new Error('--provider 只允许 SqlServer 或 MySql。');
  }
  if (result.normalizeOnly && (!result.input || !result.output)) {
    throw new Error('--normalize-only 必须同时提供 --input 与 --output。');
  }
  return result;
}

async function readJson(filePath) {
  return JSON.parse(await readFile(filePath, 'utf8'));
}

function isNonEmptyString(value) {
  return typeof value === 'string' && value.length > 0;
}
