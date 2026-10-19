import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdir, readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { testRunEnvironment, withTestRun } from '../../scripts/testing/test-run-context.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function docker(args) {
  const result = spawnSync('docker', args, { encoding: 'utf8', timeout: 180_000, maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  if (result.stdout) process.stdout.write(result.stdout);
}

if (process.argv.includes('--help')) {
  process.stdout.write('Usage: node eng/testing/otel-collector-config-test.mjs\n');
} else {
  await withTestRun({ cwd: root, heavy: true }, async () => {
    const values = await readFile(path.join(root, 'deploy/observability/otel-collector-values.yaml'), 'utf8');
    const repository = /^  repository: (\S+)$/mu.exec(values)?.[1];
    const tag = /^  tag: "([^"\r\n]+)"$/mu.exec(values)?.[1];
    const digest = /^  digest: (sha256:[a-f0-9]{64})$/mu.exec(values)?.[1];
    assert.ok(repository && tag && digest, 'Collector 必须固定版本与摘要。');
    const image = `${repository}:${tag}@${digest}`;
    if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
    // 直接提取部署 config，严格校验由真实二进制负责；不以字段存在性证明配置可用。
    const lines = values.split(/\r?\n/u);
    const start = lines.indexOf('config:');
    assert.ok(start >= 0);
    const config = [];
    for (const line of lines.slice(start + 1)) {
      if (/^[^\s#]/u.test(line)) break;
      config.push(line.replace(/^  /u, ''));
    }
    assert.ok(config.some(line => line === 'service:'));
    const file = path.join(testRunEnvironment().TEMP, 'otel-collector.yaml');
    const storage = path.join(testRunEnvironment().TEMP, 'storage');
    // 与 Helm 的 emptyDir 挂载保持一致；不因裸容器缺少挂载而误报配置失败。
    await mkdir(storage);
    await writeFile(file, config.join('\n'));
    docker(['run', '--rm', '--cpus', '1', '--memory', '256m',
      '--mount', `type=bind,source=${file},target=/work/collector.yaml,readonly`,
      '--mount', `type=bind,source=${storage},target=/var/otel-collector/storage`,
      image, 'validate', '--config=/work/collector.yaml']);
    process.stdout.write('Collector 实际部署 config 严格校验通过；不等同下游投递或 Helm 安装通过。\n');
  });
}
