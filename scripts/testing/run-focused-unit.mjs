import { readFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

/** 手动入口只接收一个明确测试类，禁止空筛选、通配符及复合表达式退化为全量。 */
export function focusedUnitSelection(targetClass, minimumTests, methodName = '') {
  if (!/^Full\.NET\.UnitTests(?:\.[A-Za-z_][A-Za-z0-9_]*){2,}$/u.test(targetClass ?? '')) {
    throw new Error('必须提供 Full.NET.UnitTests 下的完整测试类名。');
  }
  if (!/^[1-9][0-9]*$/u.test(minimumTests ?? '') || !Number.isSafeInteger(Number(minimumTests))) {
    throw new Error('最低发现数必须是正整数。');
  }
  const methodNames = methodName === '' ? [] : methodName.split(',').map(value => value.trim());
  if (methodNames.length > 10 || new Set(methodNames).size !== methodNames.length
    || methodNames.some(name => !/^[A-Za-z_][A-Za-z0-9_]*$/u.test(name))) throw new Error('测试方法必须是最多十个不重复的精确标识符。');
  const parts = targetClass.split('.');
  return {
    sourcePath: `tests/Full.NET.UnitTests/${parts.slice(3).join('/')}.cs`,
    namespace: parts.slice(0, -1).join('.'),
    className: parts.at(-1),
    methodNames,
    // 保留类型后的分隔符，不能顺带匹配名称具有相同前缀的其他测试类。
    args: ['unit', '--filter', methodNames.length ? methodNames.map(name => `FullyQualifiedName=${targetClass}.${name}`).join('|') : `FullyQualifiedName~${targetClass}.`,
      '--minimum-expected-tests', minimumTests]
  };
}

/** 源文件必须声明同名正式测试类；失配时在昂贵构建之前失败。 */
export function validateFocusedUnitSource(selection, source) {
  const namespacePattern = selection.namespace.replaceAll('.', '\\.');
  if (!new RegExp(`\\bnamespace\\s+${namespacePattern}\\s*[;{]`, 'u').test(source)
    || !new RegExp(`\\[TestClass\\]\\s*(?:public\\s+)?(?:sealed\\s+)?(?:partial\\s+)?class\\s+${selection.className}\\b`, 'u').test(source)) {
    throw new Error('指定文件未声明匹配的正式测试类。');
  }
  for (const methodName of selection.methodNames) {
    if (!new RegExp(`\\[TestMethod\\][^{};]*\\b${methodName}\\s*\\(`, 'u').test(source)) throw new Error('指定文件未声明匹配的正式测试方法。');
  }
}

async function main() {
  if (process.argv.slice(2).some(argument => argument !== '--validate')) throw new Error('不支持的执行参数。');
  const selection = focusedUnitSelection(process.env.TARGET_CLASS, process.env.MINIMUM_TESTS, process.env.TARGET_METHOD ?? '');
  validateFocusedUnitSource(selection, await readFile(path.join(repositoryRoot, selection.sourcePath), 'utf8'));
  console.log(`聚焦测试：${process.env.TARGET_CLASS}${selection.methodNames.length ? ' [' + selection.methodNames.join(', ') + ']' : ''}，最低发现数：${process.env.MINIMUM_TESTS}`);
  if (process.argv.includes('--validate')) return;
  const result = spawnSync(process.execPath, [path.join(repositoryRoot, 'scripts/testing/run-dotnet-test-suite.mjs'), ...selection.args],
    { cwd: repositoryRoot, stdio: 'inherit', env: process.env });
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main().catch(error => { console.error(error.message); process.exitCode = 1; });
}
