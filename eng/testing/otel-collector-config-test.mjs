import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFile, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { setTimeout as sleep } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { testRunEnvironment, withTestRun } from '../../scripts/testing/test-run-context.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

function docker(args, includeStderr = false) {
  const result = spawnSync('docker', args, { encoding: 'utf8', timeout: 180_000, maxBuffer: 1024 * 1024 });
  assert.equal(result.status, 0, `${result.stdout ?? ''}\n${result.stderr ?? ''}\n${result.error?.message ?? ''}`);
  return (includeStderr ? result.stdout + result.stderr : result.stdout).trim();
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
    const fsGroup = /^podSecurityContext:\r?\n  fsGroup: ([1-9]\d*)$/mu.exec(values)?.[1];
    assert.ok(fsGroup, 'Collector 的 emptyDir 必须授予非 root 存储组写权限。');
    const image = `${repository}:${tag}@${digest}`;
    if (spawnSync('docker', ['image', 'inspect', image], { stdio: 'ignore' }).status !== 0) docker(['pull', image]);
    // 直接提取部署 config，严格校验由真实二进制负责；不以字段存在性证明配置可用。
    const lines = values.split(/\r?\n/u);
    const start = lines.indexOf('config:');
    assert.ok(start >= 0);
    const config = [];
    for (const line of lines.slice(start + 1)) {
      if (/^[^\s#]/u.test(line)) break;
      // Helm 合并时用 null 删除默认日志管道；直接加载 Collector YAML 时省略该删除指令。
      if (line === '      logs: null') continue;
      config.push(line.replace(/^  /u, ''));
    }
    assert.ok(config.some(line => line === 'service:'));
    const file = path.join(testRunEnvironment().TEMP, 'otel-collector.yaml');
    await writeFile(file, config.join('\n'));
    // 本地容器模拟 kubelet 给 emptyDir 应用 fsGroup 后的组权限，保留镜像自身的非 root 用户。
    const containerOptions = ['--cpus', '1', '--memory', '256m', '--group-add', fsGroup,
      '--mount', `type=bind,source=${file},target=/work/collector.yaml,readonly`,
      '--tmpfs', `/var/otel-collector/storage:rw,uid=0,gid=${fsGroup},mode=2770`];
    docker(['run', '--rm', ...containerOptions, image, 'validate', '--config=/work/collector.yaml']);
    const container = docker(['run', '--detach', ...containerOptions, '--publish', '127.0.0.1::13133',
      image, '--config=/work/collector.yaml']);
    try {
      const endpoint = docker(['port', container, '13133/tcp']);
      for (let attempt = 0; attempt < 40; attempt++) {
        const response = await fetch(`http://${endpoint}/`, { signal: AbortSignal.timeout(1_000) }).catch(() => null);
        if (response?.ok) {
          process.stdout.write('Collector 部署 config 严格校验及非 root 启动检查通过。\n');
          return;
        }
        if (docker(['inspect', '--format', '{{.State.Running}}', container]) !== 'true') break;
        await sleep(250);
      }
      assert.fail(`Collector 未就绪。\n${docker(['logs', '--tail', '20', container], true)}`);
    } finally {
      docker(['rm', '--force', '--volumes', container]);
    }
  });
}
