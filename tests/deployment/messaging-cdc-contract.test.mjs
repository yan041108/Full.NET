import assert from 'node:assert/strict';
import { readFile, access } from 'node:fs/promises';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../..'
);

async function read(relativePath) {
  return readFile(path.join(repositoryRoot, relativePath), 'utf8');
}

async function exists(relativePath) {
  try {
    await access(path.join(repositoryRoot, relativePath));
    return true;
  } catch {
    return false;
  }
}

async function readJson(relativePath) {
  return JSON.parse(await read(relativePath));
}

const messagingFiles = [
  'deploy/messaging/README.md',
  'deploy/messaging/compose.kafka-debezium.yml',
  'deploy/messaging/connectors/sqlserver-outbox-shadow.json',
  'deploy/messaging/connectors/mysql-outbox-shadow.json',
  'deploy/messaging/sqlserver/enable-outbox-cdc.sql',
  'deploy/messaging/sqlserver/disable-outbox-cdc.sql',
  'deploy/messaging/mysql/verify-binlog.sql',
];

test('messaging CDC deploy artifacts exist', async () => {
  for (const file of messagingFiles) {
    assert.equal(await exists(file), true, `${file} missing`);
    const text = await read(file);
    assert.ok(text.trim().length > 0, `${file} empty`);
  }
});

test('compose pins Kafka 4.1.2 and Debezium 3.4.3.Final without :latest', async () => {
  const compose = await read('deploy/messaging/compose.kafka-debezium.yml');
  assert.match(compose, /apache\/kafka:4\.1\.2/);
  assert.match(compose, /quay\.io\/debezium\/connect:3\.4\.3\.Final/);
  assert.doesNotMatch(compose, /image:\s*[^\n#]*:latest\b/i);
  assert.match(compose, /FULLNET_SQLSERVER_PASSWORD/);
  assert.match(compose, /FULLNET_MYSQL_PASSWORD/);
  assert.match(compose, /replace-me/);
  assert.match(compose, /fullnet\.dev\.shadow\.internal\./);
});

test('SQL Server shadow connector scopes capture to append-only outbox', async () => {
  const connector = await readJson(
    'deploy/messaging/connectors/sqlserver-outbox-shadow.json'
  );
  const config = connector.config;
  assert.equal(config['snapshot.mode'], 'no_data');
  assert.equal(config['table.include.list'], 'dbo.fn_messaging_outbox_event');
  assert.equal(config['capture.instance'], 'fullnet_fn_messaging_outbox_event');
  assert.match(
    config['transforms.outbox.route.topic.replacement'],
    /^fullnet\.dev\.shadow\.\$\{routedByValue\}$/
  );
  assert.match(config['schema.history.internal.kafka.topic'], /fullnet\.dev\.shadow\.internal\./);
  assert.match(config['heartbeat.topics.prefix'], /fullnet\.dev\.shadow\.internal\./);
  assert.doesNotMatch(JSON.stringify(config), /fn_messaging_inbox_message/);
  assert.doesNotMatch(JSON.stringify(config), /fn_outbox_message/);
});

test('MySQL shadow connector requires ROW binlog semantics via runbook and scopes outbox only', async () => {
  const connector = await readJson(
    'deploy/messaging/connectors/mysql-outbox-shadow.json'
  );
  const config = connector.config;
  assert.equal(config['snapshot.mode'], 'no_data');
  assert.match(config['table.include.list'], /fn_messaging_outbox_event$/);
  assert.doesNotMatch(config['table.include.list'], /fn_messaging_inbox_message/);
  assert.match(
    config['transforms.outbox.route.topic.replacement'],
    /^fullnet\.dev\.shadow\.\$\{routedByValue\}$/
  );
  assert.match(config['schema.history.internal.kafka.topic'], /fullnet\.dev\.shadow\.internal\./);

  const verify = await read('deploy/messaging/mysql/verify-binlog.sql');
  assert.match(verify, /log_bin/);
  assert.match(verify, /binlog_format/);
  assert.match(verify, /binlog_row_image/);
  assert.match(verify, /ROW/i);
  assert.match(verify, /FULL/i);
});

test('SQL Server CDC scripts use stable capture instance and are not DbUp migrations', async () => {
  const enable = await read('deploy/messaging/sqlserver/enable-outbox-cdc.sql');
  const disable = await read('deploy/messaging/sqlserver/disable-outbox-cdc.sql');
  assert.match(enable, /fullnet_fn_messaging_outbox_event/);
  assert.match(enable, /sp_cdc_enable_table/);
  assert.match(enable, /fn_messaging_outbox_event/);
  assert.match(disable, /sp_cdc_disable_table/);
  assert.match(disable, /fullnet_fn_messaging_outbox_event/);

  const readme = await read('deploy/messaging/README.md');
  assert.match(readme, /NOT DbUp|not DbUp|禁止由 DbUp/i);
  assert.match(readme, /privileged|特权/i);
  assert.match(readme, /no business Consumer|无业务 Consumer/i);
  assert.match(readme, /scan|SBOM/i);
});

test('application Helm chart does not ship Debezium or Kafka Connect images', async () => {
  const helmPaths = [
    'deploy/helm/fullnet/values.yaml',
    'deploy/helm/fullnet/values.schema.json',
    'deploy/helm/fullnet/templates/worker-deployment.yaml',
    'deploy/helm/fullnet/templates/configmap.yaml',
  ];
  for (const file of helmPaths) {
    const text = await read(file);
    assert.doesNotMatch(text, /quay\.io\/debezium/i);
    assert.doesNotMatch(text, /debezium\/connect/i);
  }
});