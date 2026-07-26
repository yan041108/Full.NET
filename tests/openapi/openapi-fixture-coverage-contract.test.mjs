import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractsDirectory = path.join(repositoryRoot, 'contracts/openapi');
const testsDirectory = path.join(repositoryRoot, 'tests/openapi');
const coverageTestFileName = path.basename(fileURLToPath(import.meta.url));

test('每个 OpenAPI 夹具都由离线契约测试引用', async () => {
  const contractFileNames = (await readdir(contractsDirectory))
    .filter(fileName => fileName.endsWith('.json'))
    .sort();
  const contractTestFileNames = (await readdir(testsDirectory))
    .filter(fileName => fileName.endsWith('-contract.test.mjs'))
    .filter(fileName => fileName !== coverageTestFileName);
  const contractTestSources = await Promise.all(
    contractTestFileNames.map(fileName =>
      readFile(path.join(testsDirectory, fileName), 'utf8'))
  );

  const uncoveredContractFileNames = contractFileNames.filter(
    contractFileName =>
      !contractTestSources.some(source => source.includes(contractFileName))
  );

  assert.deepEqual(
    uncoveredContractFileNames,
    [],
    `以下 OpenAPI 夹具未纳入离线契约测试：${uncoveredContractFileNames.join(', ')}`
  );
});
