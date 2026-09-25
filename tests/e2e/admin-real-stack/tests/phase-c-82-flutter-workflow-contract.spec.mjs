import { expect, test } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const flutterClientDir = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../../../clients/flutter'
);

test('契约：Flutter 工作流待办 API 消费与权限边界（清单 82）', () => {
  const result = spawnSync('pnpm', ['test'], {
    cwd: flutterClientDir,
    shell: true,
    encoding: 'utf8'
  });
  if (result.status !== 0) {
    // eslint-disable-next-line no-console
    console.error(result.stdout ?? '');
    // eslint-disable-next-line no-console
    console.error(result.stderr ?? '');
  }
  expect(result.status).toBe(0);
});
