import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function readRequired(relativePath) {
  try {
    return await readFile(path.join(repositoryRoot, relativePath), 'utf8');
  } catch {
    assert.fail(`缺少 Native AOT 治理文件：${relativePath}`);
  }
}

test('Native AOT 强制规则被 AGENTS 与规则索引收录', async () => {
  const [agents, index] = await Promise.all([
    readRequired('AGENTS.md'),
    readRequired('rules/README.md')
  ]);

  assert.match(agents, /rules\/native-aot\.md/);
  assert.match(index, /\(native-aot\.md\)/);
});

test('Native AOT 规则固定静态绑定与发布完成边界', async () => {
  const rules = await readRequired('rules/native-aot.md');

  for (const required of [
    'JsonSerializerContext',
    'DynamicParameters',
    'IReadOnlyDictionary<string, object?>',
    'DapperAotMaterializerRegistry',
    'NoWarn=IL*',
    'Aot-analysis-clean',
    'Aot-published',
    'Native-provider-verified: s3',
    'Native-provider-verified: kafka-replay'
  ]) {
    assert.ok(rules.includes(required), `Native AOT 规则缺少边界：${required}`);
  }

  assert.doesNotMatch(
    rules,
    /\b(?:29\/29|5\/5|2\/2)\b|--minimum-expected-tests\s+\d+/,
    '规则不得复制 test-matrix 中的可变测试数量'
  );
});

test('Native AOT 知识库只引用真实命令与官方资料', async () => {
  const [guide, packageSource] = await Promise.all([
    readRequired('docs/development/native-aot-development-guide.md'),
    readRequired('package.json')
  ]);
  const packageJson = JSON.parse(packageSource);
  const referencedCommands = [
    ...guide.matchAll(/pnpm\s+(test:aot:[a-z0-9:-]+)/g)
  ].map(match => match[1]);

  assert.ok(referencedCommands.length >= 5, '知识库必须给出完整 Native AOT 验证梯度');
  for (const command of new Set(referencedCommands)) {
    assert.equal(
      typeof packageJson.scripts[command],
      'string',
      `知识库引用了不存在的 package script：${command}`
    );
  }

  for (const officialUrl of [
    'https://learn.microsoft.com/dotnet/core/deploying/native-aot/',
    'https://learn.microsoft.com/aspnet/core/fundamentals/native-aot/',
    'https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation',
    'https://learn.microsoft.com/dotnet/core/deploying/trimming/prepare-libraries-for-trimming'
  ]) {
    assert.ok(guide.includes(officialUrl), `知识库缺少官方依据：${officialUrl}`);
  }
});

test('Native AOT Provider 声明保持 ADR-0009 的精确范围', async () => {
  const [rules, guide] = await Promise.all([
    readRequired('rules/native-aot.md'),
    readRequired('docs/development/native-aot-development-guide.md')
  ]);
  const combined = `${rules}\n${guide}`;

  assert.match(combined, /ADR-0009-host-api-native-aot-provider-runtime-boundary\.md/);
  assert.match(combined, /Worker/);
  assert.match(combined, /CDC Relay/);
  assert.match(combined, /DLQ/);
  assert.match(combined, /Lag Observer/);
});
