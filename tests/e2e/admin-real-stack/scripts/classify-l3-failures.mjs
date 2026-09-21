/**
 * 解析 Playwright JSON 报告，按 A–F 分类失败用例，写入 Markdown。
 */
import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const rootDir = join(dirname(fileURLToPath(import.meta.url)), '..');
const defaultJsonPath = join(rootDir, '.artifacts/playwright-l3-report.json');
const outMdPath = join(rootDir, '.artifacts/host-admin-l3-failures.md');

const CATEGORY_LABELS = {
  A: 'Vite/API 同源（登录、CSRF、client.login_failed）',
  B: 'Redis / health ready',
  C: 'Worker / Jobs / 通知后台',
  D: '导航污染 / catalog 白名单',
  E: 'OIDC Center / SSO Origin',
  F: '断言或种子数据漂移',
  O: '其他'
};

function classifyError(message, file) {
  const text = `${message ?? ''} ${file ?? ''}`.toLowerCase();
  if (
    text.includes('login_failed')
    || text.includes('主导航')
    || (text.includes('navigation') && text.includes('visible'))
    || text.includes('csrf')
    || text.includes('origin_not_allowed')
    || text.includes('进入控制台')
  ) {
    if (text.includes('25175') || text.includes('oidc-center') || file?.includes('admin-oidc-center')) {
      return 'E';
    }
    if (text.includes('supportednavigationtree') || text.includes('navigation') && text.includes('catalog')) {
      return 'D';
    }
    return 'A';
  }
  if (text.includes('health/ready') || text.includes('redis') || text.includes('not healthy')) {
    return 'B';
  }
  if (text.includes('worker') || text.includes('outbox') || text.includes('job') && text.includes('timeout')) {
    return 'C';
  }
  if (text.includes('e2e_probe') || text.includes('e2e-probe') || text.includes('isSupportedNavigationTree')) {
    return 'D';
  }
  if (
    file?.includes('admin-oidc-center')
    || file?.includes('identity-oidc-sso')
    || text.includes('oidc')
    && (text.includes('origin') || text.includes('25175'))
  ) {
    return 'E';
  }
  if (
    text.includes('系统管理员')
    || text.includes('工作台')
    || text.includes('menus-tree-table')
    || text.includes('tohavetext')
    || text.includes('tocontaintext')
  ) {
    return 'F';
  }
  if (text.includes('codegeneration') || text.includes('workspace') || text.includes('stack-state')) {
    return 'O';
  }
  return 'O';
}

function flattenSuites(suites, projectName = '', file = '', out = []) {
  for (const suite of suites ?? []) {
    const suiteFile = suite.file ?? file;
    const suiteProject = suite.projectName ?? projectName;
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        for (const result of test.results ?? []) {
          if (result.status === 'failed' || result.status === 'timedOut') {
            const errors = (result.errors ?? []).map(e => e.message ?? e.value ?? '').join('\n');
            out.push({
              project: suiteProject,
              file: suiteFile,
              title: [...(suite.titlePath ?? []), spec.title, test.title].filter(Boolean).join(' › '),
              status: result.status,
              message: errors.slice(0, 800)
            });
          }
        }
      }
    }
    flattenSuites(suite.suites, suiteProject, suiteFile, out);
  }
  return out;
}

/**
 * @param {string} [jsonPath]
 * @returns {{ failures: object[], byCategory: Map<string, object[]>, markdown: string }}
 */
export function classifyL3Failures(jsonPath = defaultJsonPath) {
  if (!existsSync(jsonPath)) {
    const markdown = '# L3 失败分类\n\n未找到 Playwright JSON 报告：`playwright-l3-report.json`。请先运行 L3 全量。\n';
    writeFileSync(outMdPath, markdown, 'utf8');
    return { failures: [], byCategory: new Map(), markdown };
  }

  const report = JSON.parse(readFileSync(jsonPath, 'utf8'));
  const failures = flattenSuites(report.suites);
  const byCategory = new Map();
  for (const row of failures) {
    const cat = classifyError(row.message, row.file);
    row.category = cat;
    if (!byCategory.has(cat)) {
      byCategory.set(cat, []);
    }
    byCategory.get(cat).push(row);
  }

  const lines = [
    '# L3 失败分类',
    '',
    `生成时间：${new Date().toISOString()}`,
    '',
    `失败用例数：**${failures.length}**`,
    '',
    '## 按类别统计',
    '',
    '| 类别 | 说明 | 数量 |',
    '|------|------|------|'
  ];
  for (const key of ['A', 'B', 'C', 'D', 'E', 'F', 'O']) {
    const count = byCategory.get(key)?.length ?? 0;
    if (count > 0) {
      lines.push(`| ${key} | ${CATEGORY_LABELS[key]} | ${count} |`);
    }
  }
  lines.push('', '## 明细', '');
  for (const key of ['A', 'B', 'C', 'D', 'E', 'F', 'O']) {
    const items = byCategory.get(key);
    if (!items?.length) {
      continue;
    }
    lines.push(`### ${key} — ${CATEGORY_LABELS[key]}`, '');
    for (const item of items) {
      lines.push(`- **${item.title}**`);
      lines.push(`  - 项目：\`${item.project}\` · 文件：\`${item.file}\``);
      const snippet = item.message.replace(/\s+/g, ' ').slice(0, 200);
      if (snippet) {
        lines.push(`  - 错误：${snippet}`);
      }
    }
    lines.push('');
  }

  lines.push('## 修复状态（摘要）', '');
  lines.push(
    '- **已落地**：Playwright 走 Vite 代理（`VITE_API_PROXY_TARGET`）、`preflight-local-stack`、JSON 分类报告、`resolve-attach-stack-env`、CodeGen 附着 skip。'
  );
  lines.push(
    '- **仍开放**：附着栈需已迁移+Development 种子库、Worker（或跳过依赖项）、`provision-viewer`（global-setup 附着模式已自动调用）、OIDC Center 种子客户端；全量通过前请按上表 A→E→F 逐批处理。'
  );
  lines.push('');

  const markdown = lines.join('\n');
  writeFileSync(outMdPath, markdown, 'utf8');
  return { failures, byCategory, markdown, outPath: outMdPath };
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1].replace(/\\/g, '/')) {
  const path = process.argv[2] ?? defaultJsonPath;
  const result = classifyL3Failures(path);
  console.log(`Wrote ${result.outPath ?? outMdPath} (${result.failures.length} failures)`);
}
