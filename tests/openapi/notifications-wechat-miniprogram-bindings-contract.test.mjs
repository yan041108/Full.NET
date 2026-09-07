import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-wechat-miniprogram-bindings-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/WeChatMiniProgramBindingContracts.cs'
);
const endpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageWeChatMiniProgramBindings/Endpoint.cs'
);

test('微信小程序绑定 OpenAPI 夹具与 C# 契约和端点一致', async () => {
  const contract = JSON.parse(await readFile(contractPath, 'utf8'));
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointSourcePath, 'utf8');
  assert.equal(contract.id, 'notifications-wechat-miniprogram-bindings-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/notifications\/wechat-miniprogram\/bindings"\)/u);
  assert.match(endpointSource, /WithName\("notificationsListWeChatMiniProgramBindings"\)/u);
  assert.match(contractsSource, /record WeChatMiniProgramBindingResponse/u);
  assert.match(contractsSource, /notifications\.wechat_miniprogram_bindings\.read/u);
  assert.match(contractsSource, /notifications\.wechat_miniprogram_bindings\.bind/u);
});
