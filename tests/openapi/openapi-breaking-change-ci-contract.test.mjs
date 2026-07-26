import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

test('package 与 pull request/push main CI 执行 OpenAPI 向后兼容门禁', async () => {
  const packageJson = JSON.parse(
    await readFile(path.join(repositoryRoot, 'package.json'), 'utf8')
  );
  const workflow = await readFile(
    path.join(repositoryRoot, '.github/workflows/ci.yml'),
    'utf8'
  );
  const clientBuildJob = workflow.slice(
    workflow.indexOf('  client-build-test:'),
    workflow.indexOf('  real-stack-e2e:')
  );

  assert.equal(
    packageJson.scripts['test:openapi:breaking'],
    'node scripts/openapi/check-openapi-breaking-changes.mjs'
  );
  assert.match(
    clientBuildJob,
    /uses: actions\/checkout@v4\s+with:\s+fetch-depth: 0/u
  );
  assert.match(
    clientBuildJob,
    /name: Verify OpenAPI backward compatibility\s+if: github\.event_name == 'pull_request' \|\| github\.event\.before != '0000000000000000000000000000000000000000'\s+env:\s+OPENAPI_BASE_REF: \$\{\{ github\.event_name == 'pull_request' && github\.event\.pull_request\.base\.sha \|\| github\.event\.before \}\}\s+run: pnpm test:openapi:breaking -- --base-ref "\$OPENAPI_BASE_REF"/u
  );
});
