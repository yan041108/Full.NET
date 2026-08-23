import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { apiNativeAotPublishContract } from '../../scripts/testing/api-native-aot-publish-contract.mjs';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

test('package.json 暴露稳定的 linux Native AOT publish 命令', async () => {
  const packageJson = JSON.parse(await read('package.json'));
  assert.equal(
    packageJson.scripts['test:aot:publish:linux'],
    'node scripts/testing/run-api-aot-publish-linux.mjs'
  );
});

test('test-matrix 登记 Native AOT publish 产物下限', async () => {
  const matrix = JSON.parse(await read('eng/testing/test-matrix.json'));
  const publishGate = matrix.nativeAotPublish;
  assert.ok(publishGate, 'eng/testing/test-matrix.json 必须包含 nativeAotPublish 节');
  assert.equal(
    publishGate.minimumExecutableBytes,
    apiNativeAotPublishContract.minimumExecutableBytes
  );
  assert.equal(
    publishGate.runtimeIdentifier,
    apiNativeAotPublishContract.runtimeIdentifier
  );
  assert.equal(
    publishGate.executableRelativePath.replace(/\\/g, '/'),
    path
      .join(
        apiNativeAotPublishContract.outputRelativeDir,
        apiNativeAotPublishContract.executableName
      )
      .replace(/\\/g, '/')
  );
});

test('publish 脚本与契约包含完整 MSBuild 参数', async () => {
  const script = await read('scripts/testing/run-api-aot-publish-linux.mjs');
  const contractSource = await read(
    'scripts/testing/api-native-aot-publish-contract.mjs'
  );
  assert.match(script, /api-native-aot-publish-contract\.mjs/);
  assert.match(contractSource, /FullNetPublishMode:\s*'NativeAot'/);
  assert.match(contractSource, /linux-x64/);
  assert.match(contractSource, /SelfContained:\s*'true'/);
});
