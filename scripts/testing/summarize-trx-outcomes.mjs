import { readFileSync } from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

function decodeXml(value) {
  return value
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&');
}

function attributes(tag) {
  const result = {};
  for (const match of tag.matchAll(/([\w:.-]+)="([^"]*)"/g)) {
    result[match[1]] = decodeXml(match[2]);
  }
  return result;
}

export function summarizeTrxOutcomes(xml) {
  const outcomes = {
    Passed: 0,
    Failed: 0,
    Inconclusive: 0,
    Skipped: 0,
    NotExecuted: 0,
    Other: 0
  };
  const inconclusiveTests = [];

  for (const match of xml.matchAll(/<UnitTestResult\b([^>]*)\/?>/g)) {
    const result = attributes(match[1]);
    const outcome = result.outcome ?? 'Other';
    if (Object.hasOwn(outcomes, outcome)) {
      outcomes[outcome] += 1;
    } else {
      outcomes.Other += 1;
    }
    if (outcome === 'Inconclusive') {
      inconclusiveTests.push(result.testName ?? result.testId ?? 'Unknown');
    }
  }

  const total = Object.values(outcomes).reduce((sum, count) => sum + count, 0);
  return { total, outcomes, inconclusiveTests };
}

export function renderSummary(report) {
  const lines = [
    '# Integration TRX 结果摘要',
    '',
    `- Total: ${report.total}`,
    `- Passed: ${report.outcomes.Passed}`,
    `- Failed: ${report.outcomes.Failed}`,
    `- Inconclusive: ${report.outcomes.Inconclusive}`,
    `- Skipped: ${report.outcomes.Skipped}`,
    `- NotExecuted: ${report.outcomes.NotExecuted}`
  ];
  if (report.inconclusiveTests.length > 0) {
    lines.push('', '## Inconclusive 测试', '');
    for (const name of report.inconclusiveTests) {
      lines.push(`- ${name}`);
    }
  }
  return lines.join('\n');
}

function main(argv) {
  const trxPath = argv[0];
  if (!trxPath) {
    throw new Error('用法：node summarize-trx-outcomes.mjs <path-to.trx> [--fail-on-inconclusive]');
  }

  const failOnInconclusive = argv.includes('--fail-on-inconclusive');
  const absolutePath = path.resolve(trxPath);
  const report = summarizeTrxOutcomes(readFileSync(absolutePath, 'utf8'));
  process.stdout.write(`${renderSummary(report)}\n`);

  if (failOnInconclusive && report.outcomes.Inconclusive > 0) {
    throw new Error(
      `检测到 ${report.outcomes.Inconclusive} 项 Inconclusive；nightly 外部 SQL Server 路径必须 Pass/Fail。`
    );
  }
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  try {
    main(process.argv.slice(2));
  } catch (error) {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  }
}
