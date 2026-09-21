import { l3SpecsForComponentKey } from './qa-l3-mapping.mjs';

function p0Summary(p0) {
  if (!p0) {
    return '—';
  }
  const parts = [];
  if (p0.reachability === 'fail') {
    parts.push('路由');
  }
  if (p0.api5xx === 'fail') {
    parts.push('5xx');
  }
  if (p0.query === 'fail') {
    parts.push('查询');
  }
  if (p0.create === 'fail') {
    parts.push('创建');
  }
  if (p0.create === 'needs_scenario') {
    parts.push('创建需场景');
  }
  if (p0.create === 'na' || p0.create === 'readOnly') {
    parts.push('只读');
  }
  if (parts.length === 0 && p0.overall === 'pass') {
    return '通过';
  }
  if (p0.overall === 'pass') {
    return '通过';
  }
  if (p0.overall === 'blocked') {
    return '阻塞';
  }
  return parts.join('/') || p0.overall || '—';
}

function p1Summary(p1) {
  if (!p1) {
    return '—';
  }
  const row = p1.row?.status ?? 'na';
  const exp = p1.export?.status ?? 'na';
  if (row === 'pass' || exp === 'pass') {
    return '部分通过';
  }
  if (row === 'fail' || exp === 'fail') {
    return '失败';
  }
  return 'N/A';
}

/**
 * @param {object} audit - host-admin-qa-audit.json 根对象
 */
export function buildQaReportMarkdown(audit) {
  const lines = [];
  lines.push('# Host 管理端功能测试报告', '');
  lines.push(`**执行时间**：${audit.auditedAt}`, '');
  lines.push('**范围**：Host 超级管理员 · navigation 菜单全量 · 风险分级 P0/P1', '');
  lines.push(
    `**环境**：API \`${audit.environment?.apiBaseUrl ?? ''}\` · Admin \`${audit.environment?.adminBaseUrl ?? ''}\``,
    ''
  );

  const t = audit.totals ?? {};
  lines.push('## 1. 摘要', '');
  lines.push('| 指标 | 数量 |', '|------|------|');
  lines.push(`| 菜单页总数 | ${t.pages ?? 0} |`);
  lines.push(`| P0 通过 | ${t.p0Pass ?? 0} |`);
  lines.push(`| P0 失败 | ${t.p0Fail ?? 0} |`);
  lines.push(`| P0 只读/无创建 | ${t.p0ReadOnly ?? 0} |`);
  lines.push(`| P0 需专用场景 | ${t.p0NeedsScenario ?? 0} |`);
  lines.push(`| P1 有执行且通过 | ${t.p1Pass ?? 0} |`);
  lines.push(`| P1 失败 | ${t.p1Fail ?? 0} |`);
  lines.push(`| L1 冒烟（crawl） | ${audit.l1Crawl?.summary ?? '未执行'} |`);
  const l3 = audit.l3Playwright;
  const l3Line = l3?.specFileCount
    ? `${l3.summary ?? '—'}（${l3.specFileCount} spec 文件）`
    : (l3?.summary ?? '见附录');
  lines.push(`| L3 Playwright | ${l3Line} |`, '');

  lines.push('## 2. 范围与排除', '');
  lines.push('- **P0**：路由可达、列表无 5xx、查询按钮、主路径创建（或显式只读）。');
  lines.push('- **P1**：首行安全操作（编辑/查看打开后取消）、导出触发无 5xx。');
  lines.push('- **P2（本轮未自动执行）**：批量删除、外部系统同步、工作流推进等，见 §5。', '');

  const byDomain = new Map();
  for (const row of audit.results ?? []) {
    const domain = row.domain ?? '其他';
    if (!byDomain.has(domain)) {
      byDomain.set(domain, []);
    }
    byDomain.get(domain).push(row);
  }

  lines.push('## 3. 覆盖矩阵（按域）', '');
  for (const [domain, rows] of [...byDomain.entries()].sort((a, b) => a[0].localeCompare(b[0]))) {
    lines.push(`### ${domain}`, '');
    lines.push('| 页面 | 路径 | P0 | P1 | L3 spec | 备注 |');
    lines.push('|------|------|----|----|---------|------|');
    for (const r of rows) {
      const l3 = l3SpecsForComponentKey(r.componentKey).join(', ') || '—';
      const note =
        r.status === 'needs_scenario'
          ? r.step ?? 'needs_scenario'
          : r.p0?.detail?.slice(0, 40) ?? '';
      lines.push(
        `| ${r.title} | \`${r.path}\` | ${p0Summary(r.p0)} | ${p1Summary(r.p1)} | ${l3} | ${note} |`
      );
    }
    lines.push('');
  }

  const defects = (audit.results ?? []).filter(r => r.status === 'fail' || r.p0?.overall === 'fail');
  lines.push('## 4. 缺陷清单（P0/P1）', '');
  if (defects.length === 0) {
    lines.push('_本轮无 P0/P1 阻塞缺陷。_', '');
  } else {
    let id = 1;
    for (const d of defects) {
      lines.push(
        `### DEF-${id}`,
        `- **页面**：${d.title} (\`${d.path}\`)`,
        `- **步骤**：${d.step ?? d.p0?.step ?? '—'}`,
        `- **严重度**：${d.severity ?? 'Critical'}`,
        `- **证据**：${d.evidence?.screenshot ?? d.p0?.detail ?? d.error ?? '—'}`,
        ''
      );
      id += 1;
    }
  }

  const needs = (audit.results ?? []).filter(
    r => r.status === 'needs_scenario' || r.p0?.create === 'needs_scenario'
  );
  lines.push('## 5. needs_scenario / 未覆盖按钮', '');
  if (needs.length === 0) {
    lines.push('_无。_', '');
  } else {
    for (const n of needs) {
      lines.push(`- \`${n.path}\` (${n.componentKey}) — ${n.step ?? n.p0?.step ?? ''}`);
    }
    lines.push('');
  }

  if (audit.l3Playwright?.failureClassificationPath || audit.l3Playwright?.jsonReportPath) {
    lines.push('## 6. L3 失败分类', '');
    lines.push(
      '详见 [host-admin-l3-failures.md](host-admin-l3-failures.md)（由 classify-l3-failures.mjs 根据 Playwright JSON 生成）。',
      ''
    );
    if (audit.l3Playwright?.failureCount != null) {
      lines.push(`分类失败条数：\`${audit.l3Playwright.failureCount}\``, '');
    }
  }

  lines.push('## 7. 附着本机栈检查清单（L3）', '');
  lines.push('1. `node scripts/resolve-attach-stack-env.mjs` → 在 Host.Api / Host.Worker 终端注入环境变量。');
  lines.push('2. 空库首次附着：Migrator `--seed development`（需 UuidBinaryContract / PreV1NamingContract 门禁 env，同 bootstrap-stack）。');
  lines.push('3. 推荐 Redis：`docker run -d --name fullnet-l3-redis -p 127.0.0.1:60100:6379 redis:8.6`（避免 TLS 端口 60090 误配）。');
  lines.push('4. `node scripts/preflight-local-stack.mjs` 通过后再 `node scripts/host-admin-l3-full.mjs`。');
  lines.push('5. CodeGen 附着模式无工作区时相关 spec 自动 skip；或设置 `FULLNET_E2E_CODEGEN_WORKSPACE`。', '');
  lines.push('## 8. 验证命令附录', '');
  lines.push('```bash');
  lines.push('cd tests/e2e/admin-real-stack');
  lines.push('node scripts/host-navigation-crawl.mjs');
  lines.push('node scripts/host-navigation-interaction-probe.mjs');
  lines.push('node scripts/host-admin-qa-audit.mjs');
  lines.push('pnpm exec playwright test --project=vue tests/host-users.spec.mjs  # 示例子集');
  lines.push('```', '');
  if (audit.l3Playwright?.command) {
    lines.push('**本轮 L3 命令**：', '');
    lines.push('```bash');
    lines.push('cd tests/e2e/admin-real-stack');
    lines.push(audit.l3Playwright.command);
    lines.push('# 或仅 L3：node scripts/host-admin-l3-full.mjs');
    lines.push('```', '');
    if (audit.l3Playwright.exitCode !== undefined) {
      lines.push(`退出码：\`${audit.l3Playwright.exitCode}\``, '');
    }
    if (audit.l3Playwright.specFileCount) {
      lines.push(`Spec 文件数：\`${audit.l3Playwright.specFileCount}\``, '');
    }
    if (audit.l3Playwright.passed != null) {
      lines.push(
        `用例：通过 \`${audit.l3Playwright.passed}\`，失败 \`${audit.l3Playwright.failed ?? 0}\`，跳过 \`${audit.l3Playwright.skipped ?? 0}\``,
        ''
      );
    }
  }

  return lines.join('\n');
}
