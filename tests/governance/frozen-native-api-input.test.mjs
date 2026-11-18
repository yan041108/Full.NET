import assert from 'node:assert/strict';
import test from 'node:test';
import {validateFrozenNativeApiInput} from '../../scripts/testing/frozen-native-api-input.mjs';

function input() {
  const sha = 'a'.repeat(40);
  return {
    repository: 'example/framework',
    run: {id: 42, repository: {full_name: 'example/framework'}, head_repository: {full_name: 'example/framework'},
      head_branch: 'main', path: '.github/workflows/api-native-aot-linux.yml', status: 'completed', head_sha: sha},
    artifact: {id: 7, name: 'api-native-aot-linux-manifest', expired: false,
      workflow_run: {id: 42, head_sha: sha}, digest: 'sha256:' + 'b'.repeat(64)},
    jobs: [{name: 'linux-native-aot', steps: [{name: 'Publish linux-x64 Native AOT artifact', conclusion: 'success'}]}],
    changedPaths: ['tests/NativeFixture.cs', 'docs/plan.md', 'security/client-audit-policy.json',
      'contracts/sql-safety/waivers.json',
      '.github/workflows/native-notifications-focused.yml', 'scripts/testing/frozen-native-api-input.mjs']
  };
}

test('frozen Native input accepts consumer changes with an exact producer identity and digest', () => {
  assert.deepEqual(validateFrozenNativeApiInput(input()), {artifactId: 7, digest: 'b'.repeat(64), sourceSha: 'a'.repeat(40)});
});

test('frozen Native input rejects production, build and unknown input changes', () => {
  for (const file of ['src/Host.cs', 'Directory.Build.props', 'global.json', 'Directory.Packages.props',
    'eng/docker/Dockerfile.api-native-aot-linux-sdk', 'scripts/testing/publish.mjs',
    'contracts/sql-safety/other.json', 'tests/../src/Host.cs']) {
    assert.throws(() => validateFrozenNativeApiInput({...input(), changedPaths: [file]}), /必须重新发布/u);
  }
});

test('frozen Native input rejects a different producer, source commit and missing publication', () => {
  for (const alter of [
    value => value.run.head_repository.full_name = 'fork/framework',
    value => value.run.head_branch = 'other',
    value => value.run.path = '.github/workflows/other.yml',
    value => value.run.status = 'in_progress',
    value => value.artifact.workflow_run.head_sha = 'c'.repeat(40),
    value => value.artifact.workflow_run.id = 99,
    value => value.jobs[0].steps[0].conclusion = 'failure'
  ]) {
    const value = input(); alter(value);
    assert.throws(() => validateFrozenNativeApiInput(value));
  }
});

test('frozen Native input rejects expired, renamed and unverifiable archives', () => {
  for (const alter of [
    value => value.artifact.expired = true,
    value => value.artifact.name = 'other',
    value => value.artifact.digest = undefined,
    value => value.artifact.digest = 'sha256:bad',
    value => value.artifact.id = -1
  ]) {
    const value = input(); alter(value);
    assert.throws(() => validateFrozenNativeApiInput(value));
  }
});
