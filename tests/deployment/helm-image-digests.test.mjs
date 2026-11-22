import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const chart = path.join(root, 'deploy/helm/fullnet');
const digests = { api: `sha256:${'a'.repeat(64)}`, worker: `sha256:${'b'.repeat(64)}`, migrator: `sha256:${'c'.repeat(64)}` };
const render = (role, extra = []) => spawnSync(process.env.FULLNET_HELM_BINARY ?? 'helm', ['template', 'image-digest', chart,
  '-f', path.join(chart, `ci/values-role-${role}.yaml`), '-f', path.join(chart, 'ci/values-provider-sqlserver.yaml'),
  '--set-string', 'image.repository=candidate', '--set-string', 'image.tag=logical-version', ...extra], { encoding: 'utf8' });
const valid = result => assert.equal(result.status, 0, result.stderr || result.error?.message);

// 三个角色各用自己的 manifest 摘要；改变逻辑 tag 不能改变固定的运行镜像。
for (const role of Object.keys(digests)) {
  test(`image digest: ${role} selects its own immutable manifest`, () => {
    const options = ['--set-string', 'image.registry=registry.example', '--set-json', `image.digests=${JSON.stringify(digests)}`];
    for (const tag of ['version-one', 'version-two']) {
      const result = render(role, [...options, '--set-string', `image.tag=${tag}`]); valid(result);
      assert.ok(result.stdout.includes(`image: registry.example/candidate-${role}@${digests[role]}`));
      assert.ok(!result.stdout.includes(`candidate-${role}:${tag}`));
    }
  });
}

test('image digest: omitted and explicitly empty values retain tag rendering', () => {
  for (const role of Object.keys(digests)) {
    for (const extra of [[], ['--set-string', `image.digests.${role}=`]]) {
      const result = render(role, extra); valid(result);
      assert.ok(result.stdout.includes(`image: candidate-${role}:logical-version`));
    }
  }
});

test('image digest: invalid algorithms, types and unknown roles fail schema validation', () => {
  valid(render('api'));
  for (const value of [{ api: 'sha256:short' }, { api: `sha512:${'a'.repeat(64)}` }, { api: `sha256:${'A'.repeat(64)}` },
    { api: true }, { unknown: digests.api }, [], 'invalid']) {
    const result = render('api', ['--set-json', `image.digests=${JSON.stringify(value)}`]);
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, /digests/);
  }
});

test('image digest: repository namespaces and pull policy remain intact', () => {
  const result = render('worker', ['--set-string', 'image.repository=team/candidate', '--set-string', 'image.pullPolicy=Never',
    '--set-json', `image.digests=${JSON.stringify({ worker: digests.worker })}`]); valid(result);
  assert.ok(result.stdout.includes(`image: team/candidate-worker@${digests.worker}`));
  assert.match(result.stdout, /imagePullPolicy: Never/);
});
