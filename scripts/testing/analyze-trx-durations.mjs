import { appendFile, readFile, readdir, stat } from 'node:fs/promises';
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

function durationToMilliseconds(duration) {
  const match = /^(\d+):(\d{2}):(\d{2}(?:\.\d+)?)$/.exec(duration ?? '');
  if (!match) {
    return null;
  }

  const minutes = Number(match[2]);
  const seconds = Number(match[3]);
  if (minutes >= 60 || seconds >= 60) {
    return null;
  }
  return (
    Number(match[1]) * 3_600_000
    + minutes * 60_000
    + seconds * 1_000
  );
}

function aggregate(items, keySelector) {
  const groups = new Map();
  for (const item of items) {
    const name = keySelector(item);
    const current = groups.get(name) ?? { name, count: 0, durationMs: 0 };
    current.count += 1;
    current.durationMs += item.durationMs;
    groups.set(name, current);
  }

  return [...groups.values()].sort(
    (left, right) =>
      right.durationMs - left.durationMs || left.name.localeCompare(right.name)
  );
}

function providerOf(item) {
  const identity = `${item.className} ${item.name}`;
  if (/mysql/i.test(identity)) {
    return 'MySQL';
  }

  if (/sqlserver|sql_server/i.test(identity)) {
    return 'SQL Server';
  }

  return 'Shared/Other';
}

function suiteOf(item) {
  const match = /Full\.NET\.IntegrationTests\.([^.]+)/.exec(item.className);
  return match?.[1] ?? 'Unknown';
}

export function analyzeTrx(xml) {
  const definitionsById = new Map();
  const definitionsByName = new Map();
  for (const match of xml.matchAll(/<UnitTest\b([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
    const unitTest = attributes(match[1]);
    const methodMatch = /<TestMethod\b([^>]*)\/?>/.exec(match[2]);
    const method = methodMatch ? attributes(methodMatch[1]) : {};
    const definition = {
      className: method.className ?? '',
      name: unitTest.name ?? ''
    };
    if (unitTest.id) {
      definitionsById.set(unitTest.id, definition);
    }
    if (unitTest.name) {
      definitionsByName.set(unitTest.name, definition);
    }
  }

  const tests = [];
  for (const match of xml.matchAll(/<UnitTestResult\b([^>]*)\/?>/g)) {
    const result = attributes(match[1]);
    const definition =
      definitionsById.get(result.testId)
      ?? definitionsByName.get(result.testName)
      ?? {};
    const testName = result.testName ?? definition.name ?? 'Unknown';
    const durationMs = durationToMilliseconds(result.duration);
    if (durationMs === null) {
      throw new Error(
        `测试“${testName}”缺少有效的 duration。`
      );
    }
    tests.push({
      name: testName,
      className: definition.className ?? '',
      outcome: result.outcome ?? 'Unknown',
      durationMs
    });
  }

  const passed = tests.filter(item => item.outcome === 'Passed').length;
  const failed = tests.filter(item => item.outcome === 'Failed').length;
  return {
    total: tests.length,
    passed,
    failed,
    other: tests.length - passed - failed,
    durationMs: tests.reduce((sum, item) => sum + item.durationMs, 0),
    slowest: [...tests].sort(
      (left, right) => right.durationMs - left.durationMs
    ),
    bySuite: aggregate(tests, suiteOf),
    byProvider: aggregate(tests, providerOf)
  };
}

export function formatDuration(milliseconds) {
  if (milliseconds >= 60_000) {
    const minutes = Math.floor(milliseconds / 60_000);
    const seconds = (milliseconds % 60_000) / 1_000;
    return `${minutes}m ${seconds.toFixed(3)}s`;
  }

  return `${(milliseconds / 1_000).toFixed(3)}s`;
}

function renderGroup(title, groups) {
  const lines = [`## ${title}`, ''];
  for (const group of groups) {
    lines.push(
      `- ${group.name}: ${group.count} 项，累计 ${formatDuration(group.durationMs)}`
    );
  }
  return lines;
}

export function renderReport(report, limit = 20) {
  const lines = [
    '# Integration 测试耗时摘要',
    '',
    `总计 ${report.total}，成功 ${report.passed}，失败 ${report.failed}，`
      + `其他 ${report.other}，总测试耗时 ${formatDuration(report.durationMs)}`,
    '',
    ...renderGroup('按数据库提供程序', report.byProvider),
    '',
    ...renderGroup('按测试套件', report.bySuite),
    '',
    `## 最慢 ${Math.min(limit, report.slowest.length)} 项`,
    ''
  ];

  for (const item of report.slowest.slice(0, limit)) {
    lines.push(
      `- ${formatDuration(item.durationMs)} · ${item.outcome} · ${item.name}`
    );
  }
  return `${lines.join('\n')}\n`;
}

async function collectTrxFiles(target) {
  const targetStat = await stat(target);
  if (targetStat.isFile()) {
    return target.toLowerCase().endsWith('.trx') ? [target] : [];
  }

  const files = [];
  for (const entry of await readdir(target, { withFileTypes: true })) {
    const child = path.join(target, entry.name);
    if (entry.isDirectory()) {
      files.push(...await collectTrxFiles(child));
    } else if (entry.name.toLowerCase().endsWith('.trx')) {
      files.push(child);
    }
  }
  return files;
}

async function runCli() {
  const target = path.resolve(
    process.argv[2]
      ?? 'tests/Full.NET.IntegrationTests/bin/Release/net10.0/TestResults'
  );
  const files = await collectTrxFiles(target);
  if (files.length === 0) {
    throw new Error(`未在 ${target} 找到 TRX 文件。`);
  }

  let latest = files[0];
  let latestMtime = (await stat(latest)).mtimeMs;
  for (const file of files.slice(1)) {
    const mtime = (await stat(file)).mtimeMs;
    if (mtime > latestMtime) {
      latest = file;
      latestMtime = mtime;
    }
  }

  const report = renderReport(analyzeTrx(await readFile(latest, 'utf8')));
  process.stdout.write(`${report}\n来源：${latest}\n`);
  if (process.env.GITHUB_STEP_SUMMARY) {
    await appendFile(process.env.GITHUB_STEP_SUMMARY, report, 'utf8');
  }
}

if (
  process.argv[1]
  && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  runCli().catch(error => {
    process.stderr.write(`${error.message}\n`);
    process.exitCode = 1;
  });
}
