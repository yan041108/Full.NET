import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('平台备份执行器 OpenAPI 夹具与端点一致', async () => {
  const contract = JSON.parse(await readFile(path.join(repositoryRoot, 'contracts/openapi/platform-backup-executor-v1.json'), 'utf8'));
  const endpointSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Platform/Features/ManageBackupExecutor/Endpoint.cs'), 'utf8');
  assert.equal(contract.id, 'platform-backup-executor-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/platform\/backup-executor"\)/u);
  assert.match(endpointSource, /WithName\("platformGetBackupExecutorStatus"\)/u);
});

test('国密密钥 OpenAPI 夹具与契约端点一致', async () => {
  const contract = JSON.parse(await readFile(path.join(repositoryRoot, 'contracts/openapi/cryptography-gm-keys-v1.json'), 'utf8'));
  const contractsSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Cryptography/Contracts/CryptographyContracts.cs'), 'utf8');
  const endpointSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Cryptography/Features/ManageGmKeys/Endpoint.cs'), 'utf8');
  assert.equal(contract.id, 'cryptography-gm-keys-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/cryptography"\)/u);
  assert.match(endpointSource, /WithName\("cryptographyGetStatus"\)/u);
  assert.match(contractsSource, /record CryptographyStatusResponse/u);
  assert.match(contractsSource, /cryptography\.keys\.read/u);
});

test('MQTT 控制面 OpenAPI 夹具与契约端点一致', async () => {
  const contract = JSON.parse(await readFile(path.join(repositoryRoot, 'contracts/openapi/mqtt-control-plane-v1.json'), 'utf8'));
  const contractsSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Mqtt/Contracts/MqttContracts.cs'), 'utf8');
  const endpointSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.Mqtt/Features/ManageControlPlane/Endpoint.cs'), 'utf8');
  assert.equal(contract.id, 'mqtt-control-plane-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/mqtt"\)/u);
  assert.match(endpointSource, /WithName\("mqttGetBrokerStatus"\)/u);
  assert.match(contractsSource, /record MqttBrokerStatusResponse/u);
  assert.match(contractsSource, /mqtt\.broker\.read/u);
});

test('Elasticsearch 日志管道健康 OpenAPI 夹具与端点一致', async () => {
  const contract = JSON.parse(await readFile(path.join(repositoryRoot, 'contracts/openapi/observability-elasticsearch-health-v1.json'), 'utf8'));
  const endpointSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.ObservabilityAdmin/Features/MonitorElasticsearchLogPipeline/Endpoint.cs'), 'utf8');
  const contractsSource = await readFile(path.join(repositoryRoot, 'src/Modules/Full.NET.Modules.ObservabilityAdmin/Features/MonitorElasticsearchLogPipeline/ElasticsearchLogPipelineContracts.cs'), 'utf8');
  assert.equal(contract.id, 'observability-elasticsearch-health-v1');
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/observability\/elasticsearch-log-pipeline"\)/u);
  assert.match(endpointSource, /WithName\("observabilityGetElasticsearchLogPipelineHealth"\)/u);
  assert.match(contractsSource, /record ElasticsearchLogPipelineHealthResponse/u);
});
