import assert from 'node:assert/strict';
import test from 'node:test';
import { mkdtemp, writeFile, rm, readFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { nativeAotPublishContract } from '../../scripts/testing/api-native-aot-publish-contract.mjs';
import { validatePublishWarnings } from '../../scripts/testing/api-native-aot-publish-warnings.mjs';
import { assertToolIndexesNotRuntimeInputs, nativeSourceInputs } from '../../scripts/testing/messaging-native-inputs.mjs';
import { freezeNativePublishTooling } from '../../scripts/testing/run-messaging-native-publish.mjs';

test('发布工具变化不能将旧进程记录成新策略，初始证据保持原版本', async () => {
  const directory = await mkdtemp(path.join(tmpdir(), 'fullnet-native-tooling-'));
  const files = ['run-messaging-native-publish.mjs','api-native-aot-publish-contract.mjs','api-native-aot-publish-warnings.mjs','messaging-native-inputs.mjs','native-aot-sdk-policy.mjs','test-run-context.mjs','build-resource-pool.mjs'];
  try {
    for (const file of files) await writeFile(path.join(directory,file), 'old:' + file);
    const frozen = await freezeNativePublishTooling(pathToFileURL(directory + path.sep));
    assert.deepEqual(await frozen.verify(), frozen.before);
    const original = JSON.stringify(frozen.before);
    await writeFile(path.join(directory,'messaging-native-inputs.mjs'), 'new-policy');
    await assert.rejects(frozen.verify(), /禁止旧进程声明新策略/);
    assert.equal(JSON.stringify(frozen.before), original);
    await writeFile(path.join(directory,'messaging-native-inputs.mjs'), 'old:messaging-native-inputs.mjs');
    for (const file of ['native-aot-sdk-policy.mjs','test-run-context.mjs','build-resource-pool.mjs']) {
      await writeFile(path.join(directory,file), 'changed:' + file);
      await assert.rejects(frozen.verify(), /禁止旧进程声明新策略/);
      await writeFile(path.join(directory,file), 'old:' + file);
    }
    await rm(path.join(directory,'api-native-aot-publish-contract.mjs'));
    await assert.rejects(freezeNativePublishTooling(pathToFileURL(directory + path.sep)), { code:'ENOENT' });
  } finally {
    assert.equal(path.dirname(path.resolve(directory)), path.resolve(tmpdir()));
    assert.ok(path.basename(directory).startsWith('fullnet-native-tooling-'));
    await rm(directory,{recursive:true,force:true});
  }
});

test('独立宿主使用各自项目和输出身份，未知角色失败关闭', () => {
  const roles = ['api', 'worker', 'migrator', 'log-consumer'].map(nativeAotPublishContract);
  assert.equal(new Set(roles.map(x => x.projectRelativePath)).size, 4);
  assert.equal(new Set(roles.map(x => x.outputRelativeDir)).size, 4);
  assert.equal(new Set(roles.map(x => x.executableName)).size, 4);
  assert.equal(nativeAotPublishContract('migrator').executableName, 'Full.NET.Host.Migrator');
  assert.equal(nativeAotPublishContract('log-consumer').executableName, 'Full.NET.Host.LogConsumer');
  assert.throws(() => nativeAotPublishContract('apphost'));
  assert.throws(() => nativeAotPublishContract('__proto__'));
});

test('各宿主独立构建预算且预算对象不可被运行中改写', () => {
  assert.deepEqual(nativeAotPublishContract('migrator').buildBudget, { cpu:2, memoryGiB:8 });
  assert.deepEqual(nativeAotPublishContract('api').buildBudget, { cpu:2, memoryGiB:16 });
  for (const role of ['worker','log-consumer'])
    assert.deepEqual(nativeAotPublishContract(role).buildBudget, { cpu:2, memoryGiB:12 });
  assert.throws(() => { nativeAotPublishContract('migrator').buildBudget.memoryGiB = 12; }, TypeError);
});

test('人工校对Worker原生边界有独立双库入口，不能误选普通五格式生成用例', async () => {
  const matrix = JSON.parse(await readFile(new URL('../../eng/testing/test-matrix.json', import.meta.url),'utf8'));
  const gate = matrix.integration.shards['ai-knowledge-correction-worker-native'];
  assert.equal(gate.filter, 'FullyQualifiedName~NativeWorkerCorrectionGenerationE2ETests');
  const source = await readFile(new URL('../Full.NET.IntegrationTests/NativeAot/NativeWorkerCorrectionGenerationE2ETests.cs',import.meta.url),'utf8');
  assert.equal([...source.matchAll(/\[DataRow\(DatabaseProvider\.(SqlServer|MySql)\)\]/g)].length, gate.minimum);
  assert.ok(gate.timeout.length > 0);
});

test('发布告警按实际角色收窄，不能借用其它角色或自有程序集白名单', () => {
  const warning = (assembly, code) => `/packages/${assembly}.dll : warning ${code}: bounded upstream warning`;
  assert.equal(validatePublishWarnings(warning('Confluent.Kafka', 'IL2104'), 'log-consumer').length, 1);
  assert.throws(() => validatePublishWarnings(warning('Dapper', 'IL2104'), 'log-consumer'));
  assert.throws(() => validatePublishWarnings(warning('Confluent.Kafka', 'IL2104'), 'migrator'));
  assert.throws(() => validatePublishWarnings(warning('Full.NET.Host.Migrator', 'IL2026'), 'migrator'));
  assert.throws(() => validatePublishWarnings(warning('Confluent.Kafka', 'IL3053'), 'log-consumer'));
  assert.throws(() => validatePublishWarnings('', 'apphost'));
});

test('实际ILC必须匹配独立宿主，运行内容和本次静态闭包继续冻结', () => {
  const published = [{ Identity: '/work/appsettings.json' }];
  for (const host of ['Full.NET.Host.Migrator', 'Full.NET.Host.LogConsumer']) {
    assert.doesNotThrow(() => assertToolIndexesNotRuntimeInputs(`/work/${host}.dll`, published, host));
    assert.throws(() => assertToolIndexesNotRuntimeInputs('/work/Full.NET.Host.Api.dll', published, host));
    assert.throws(() => assertToolIndexesNotRuntimeInputs(`/work/${host}.dll`, [{ Identity: '/src/contracts/database/object-comments.json' }], host));
  }
  const files = [
    'src/Hosts/Full.NET.Host.Migrator/Program.cs', 'src/Hosts/Full.NET.Host.LogConsumer/Program.cs',
    'src/BuildingBlocks/Full.NET.Seeding.Dapper/SeedExecutionStore.cs',
    'src/Modules/Full.NET.Modules.Identity/Persistence/IdentityDapperAotMaterializerContributor.Migrator.cs',
    'src/Modules/Full.NET.Modules.Tenancy/TenancyModule.cs',
    'src/Hosts/Full.NET.Host.Migrator/appsettings.json'
  ];
  const inputs = files.map(file => ({ file }));
  assert.deepEqual(nativeSourceInputs(inputs, [], 'Full.NET.Host.Migrator').map(x => x.file), files.filter(x => !x.includes('Host.LogConsumer')));
  assert.deepEqual(nativeSourceInputs(inputs, [], 'Full.NET.Host.LogConsumer').map(x => x.file), files.filter(x => x.includes('Host.Api') || x.includes('Host.Worker') || x.includes('Host.LogConsumer') || x.endsWith('.json')));
  const unrelated = [{ file:'src/Modules/Full.NET.Modules.Ai/Knowledge/AiKnowledgeCorrectionIndexService.cs' }];
  assert.deepEqual(nativeSourceInputs(unrelated), []);
  assert.deepEqual(nativeSourceInputs(inputs).map(x => x.file), files.filter(x => x.includes('Host.Api') || x.includes('Host.Worker') || x.endsWith('.json')));
  assert.throws(() => nativeSourceInputs(inputs, [], 'Full.NET.Host.AppHost'));
});
