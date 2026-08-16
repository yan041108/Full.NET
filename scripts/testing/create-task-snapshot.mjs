import path from 'node:path';
import { pathToFileURL } from 'node:url';

import { createTaskSnapshot } from './run-affected-integration.mjs';

async function run(args) {
  if (args.length > 1) {
    throw new Error('用法：pnpm test:task:start -- [task-id]');
  }

  const snapshot = await createTaskSnapshot({ id: args[0] });
  process.stdout.write(
    `任务快照已创建：${snapshot.id}\n`
    + `后续验证：pnpm test:inner -- --snapshot ${snapshot.id} --plan\n`
    + `或：pnpm test:integration:affected:plan -- --snapshot ${snapshot.id} --phase inner\n`
  );
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  run(process.argv.slice(2)).catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
