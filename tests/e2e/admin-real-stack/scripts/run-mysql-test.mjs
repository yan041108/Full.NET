import { spawnSync } from 'node:child_process';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const require = createRequire(import.meta.url);
const playwrightCli = require.resolve('@playwright/test/cli');

process.env.FULLNET_E2E_DATABASE_PROVIDER = 'MySql';

const result = spawnSync(process.execPath, [playwrightCli, 'test'], {
  cwd: packageRoot,
  env: process.env,
  stdio: 'inherit'
});

process.exit(result.status ?? 1);
