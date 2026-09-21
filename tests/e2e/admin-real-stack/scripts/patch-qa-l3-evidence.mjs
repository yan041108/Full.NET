import { readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { buildQaReportMarkdown } from './support/qa-report-builder.mjs';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const auditPath = join(root, '.artifacts/host-admin-qa-audit.json');
const audit = JSON.parse(readFileSync(auditPath, 'utf8'));
audit.l1Crawl = { summary: '97 passed, 0 failed', exitCode: 0 };
audit.l3Playwright = {
  summary: '子集 5 文件 -g 加载|列表: 3 passed, 2 failed (users 列表文案, menus 树表)',
  exitCode: 1,
  command:
    'pnpm exec playwright test --project=vue-admin tests/host-users.spec.mjs tests/host-tenants.spec.mjs tests/host-roles.spec.mjs tests/host-api-keys.spec.mjs tests/host-menus.spec.mjs -g "加载|列表"'
};
writeFileSync(auditPath, JSON.stringify(audit, null, 2), 'utf8');
writeFileSync(join(root, '.artifacts/host-admin-qa-report.md'), buildQaReportMarkdown(audit), 'utf8');
