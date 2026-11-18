import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const chart = path.join(root, 'deploy/helm/fullnet');
const helm = process.env.FULLNET_HELM_BINARY ?? 'helm';
const render = (role, extra = []) => spawnSync(helm, ['template', 'runtime-config', chart,
  '-f', path.join(chart, `ci/values-role-${role}.yaml`),
  '-f', path.join(chart, 'ci/values-provider-sqlserver.yaml'),
  '--set', 'database.connectionSecretName=selected-database', ...extra], { encoding: 'utf8', cwd: root });
const valid = result => assert.equal(result.status, 0, result.stderr || result.error?.message);

// 校验实际渲染结果，外部运行配置不得改变数据库连接的显式权威引用。
for (const role of ['api', 'worker', 'migrator']) {
  test(`runtime configuration: ${role} preserves string references and explicit database binding`, () => {
    const baseline = render(role); valid(baseline);
    assert.doesNotMatch(baseline.stdout, /(?:configMapRef|secretRef):\s+name: "(?:true|123)"/);
    const configured = render(role, ['--set-string', 'runtime.configMapName=true', '--set-string', 'runtime.secretName=123']);
    valid(configured);
    const map = configured.stdout.indexOf('configMapRef:\n                name: "true"');
    const secret = configured.stdout.indexOf('secretRef:\n                name: "123"');
    assert.ok(map >= 0 && secret > map, '运行 Secret 应覆盖外部 ConfigMap 的同名环境变量');
    assert.match(configured.stdout, /name: Database__ConnectionString\s+valueFrom:\s+secretKeyRef:\s+name: selected-database/);
  });

  test(`runtime configuration: ${role} includes extra mounts only when configured`, () => {
    const baseline = render(role); valid(baseline); assert.doesNotMatch(baseline.stdout, /runtime-extra-ca/);
    const configured = render(role, ['--set-json', 'runtime.volumes=[{"name":"runtime-extra-ca","configMap":{"name":"extra-ca"}}]',
      '--set-json', 'runtime.volumeMounts=[{"name":"runtime-extra-ca","mountPath":"/etc/fullnet/extra-ca","readOnly":true}]']);
    valid(configured);
    assert.equal((configured.stdout.match(/name: runtime-extra-ca\b/g) ?? []).length, 2);
    assert.match(configured.stdout, /mountPath: \/etc\/fullnet\/extra-ca/);
    assert.match(configured.stdout, /configMap:\s+name: extra-ca/);
  });
}

for (const [name, args] of [
  ['non-string reference', ['--set', 'runtime.configMapName=123']],
  ['non-array mounts', ['--set-json', 'runtime.volumeMounts={"name":"invalid"}']],
  ['unknown setting', ['--set', 'runtime.unrecognized=true']],
]) {
  test(`runtime configuration: rejects ${name}`, () => {
    valid(render('api'));
    const invalid = render('api', args);
    assert.notEqual(invalid.status, 0);
    assert.ok(invalid.stderr.length > 0);
  });
}
