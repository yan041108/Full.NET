import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const image = 'prom/prometheus:v3.15.0@sha256:efd719c99d83b060d9daefdcf00360461adf279f45ef5391f8d111892118753e';
const mount = `type=bind,source=${root},target=/work,readonly`;

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  if (result.stdout) process.stdout.write(result.stdout);
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/prometheus-log-alerts-test.mjs\n');
} else {
  const localImage = spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' });
  if (localImage.status !== 0) docker(['pull', image]);
  const base = ['run', '--rm', '--entrypoint', '/bin/promtool', '--mount', mount, image];
  docker([...base, 'check', 'rules', '/work/deploy/observability/prometheus-rules.yaml']);
  docker([
    'run', '--rm', '--entrypoint', '/bin/promtool', '--mount', mount,
    '--mount', `type=bind,source=${root},target=/rules,readonly`, image,
    'check', 'config', '/work/tests/deployment/fluent-bit-prometheus-scrape.yml',
  ]);
  docker([
    'run', '--rm', '--entrypoint', '/bin/promtool', '--mount', mount,
    '--workdir', '/work/tests/deployment', image,
    'test', 'rules', 'fluent-bit-output-alerts.promtest.yaml',
  ]);
  docker([
    'run', '--rm', '--entrypoint', '/bin/promtool', '--mount', mount,
    '--workdir', '/work/tests/deployment', image,
    'test', 'rules', 'log-consumer-alerts.promtest.yaml',
  ]);
}
