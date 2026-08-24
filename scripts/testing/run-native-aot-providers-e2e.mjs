#!/usr/bin/env node
/**
 * Native AOT Provider E2E：S3 + Kafka Replay。
 */
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

function runNodeScript(relativePath) {
  const scriptPath = path.join(repositoryRoot, relativePath);
  const result = spawnSync(process.execPath, [scriptPath], {
    cwd: repositoryRoot,
    encoding: 'utf8',
    stdio: 'inherit',
    shell: false,
  });
  return result.status ?? 1;
}

const s3Status = runNodeScript('scripts/testing/run-native-aot-s3-e2e.mjs');
if (s3Status !== 0) {
  process.exit(s3Status);
}

const kafkaStatus = runNodeScript(
  'scripts/testing/run-native-aot-kafka-replay-e2e.mjs'
);
process.exit(kafkaStatus);
