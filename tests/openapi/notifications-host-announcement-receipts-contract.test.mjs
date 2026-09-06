import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/notifications-host-announcement-receipts-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Contracts/AnnouncementContracts.cs'
);
const myEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageMyHostAnnouncements/Endpoint.cs'
);
const hostEndpointSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Notifications/Features/ManageHostAnnouncements/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

test('Host 公告收件与阅读统计 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'notifications-host-announcement-receipts-v1');
  assert.ok(Array.isArray(contract.paths) && contract.paths.length > 0);

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/notifications\//u);
    assert.ok(Array.isArray(entry.operations) && entry.operations.length > 0);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.match(
        operation.permission,
        /^notifications\.announcements\.(received\.(read|mark_read|mark_all_read)|read_stats)$/u
      );
      assert.ok(typeof operation.successStatus === 'number');
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.responseSchema) {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('Host 公告收件与阅读统计 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const myEndpointSource = await readFile(myEndpointSourcePath, 'utf8');
  const hostEndpointSource = await readFile(hostEndpointSourcePath, 'utf8');

  assert.match(contractsSource, /record ReceivedHostAnnouncementListItemResponse/u);
  assert.match(contractsSource, /record ReceivedHostAnnouncementDetailResponse/u);
  assert.match(contractsSource, /record HostAnnouncementUnreadCountResponse/u);
  assert.match(contractsSource, /record HostAnnouncementReadStatsResponse/u);
  assert.match(contractsSource, /record HostAnnouncementReadReceiptResponse/u);
  assert.match(
    myEndpointSource,
    /MapGroup\("\/api\/v1\/notifications\/my-host-announcements"\)/u
  );
  assert.match(myEndpointSource, /WithTags\("NotificationsMyHostAnnouncements"\)/u);
  assert.match(myEndpointSource, /WithName\("notificationsListMyHostAnnouncements"\)/u);
  assert.match(myEndpointSource, /WithName\("notificationsGetMyHostAnnouncementUnreadCount"\)/u);
  assert.match(myEndpointSource, /WithName\("notificationsGetMyHostAnnouncement"\)/u);
  assert.match(myEndpointSource, /WithName\("notificationsMarkMyHostAnnouncementRead"\)/u);
  assert.match(myEndpointSource, /WithName\("notificationsMarkAllMyHostAnnouncementsRead"\)/u);
  assert.match(hostEndpointSource, /WithName\("notificationsGetHostAnnouncementReadStats"\)/u);
  assert.match(hostEndpointSource, /WithName\("notificationsListHostAnnouncementReadReceipts"\)/u);

  const relativeRoutes = new Map([
    ['/api/v1/notifications/my-host-announcements', {
      source: myEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/",']
      ])
    }],
    ['/api/v1/notifications/my-host-announcements/unread-count', {
      source: myEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/unread-count",']
      ])
    }],
    ['/api/v1/notifications/my-host-announcements/{announcementId}', {
      source: myEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{announcementId:guid}",']
      ])
    }],
    ['/api/v1/notifications/my-host-announcements/{announcementId}/read', {
      source: myEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/{announcementId:guid}/read",']
      ])
    }],
    ['/api/v1/notifications/my-host-announcements/read-all', {
      source: myEndpointSource,
      markers: new Map([
        ['POST', 'MapPost("/read-all",']
      ])
    }],
    ['/api/v1/notifications/host-announcements/{announcementId}/read-stats', {
      source: hostEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{announcementId:guid}/read-stats",']
      ])
    }],
    ['/api/v1/notifications/host-announcements/{announcementId}/read-receipts', {
      source: hostEndpointSource,
      markers: new Map([
        ['GET', 'MapGet("/{announcementId:guid}/read-receipts",']
      ])
    }]
  ]);

  for (const entry of contract.paths) {
    const route = relativeRoutes.get(entry.path);
    assert.ok(route, `未登记的路由组：${entry.path}`);
    for (const operation of entry.operations) {
      const marker = route.markers.get(operation.method);
      assert.ok(marker, `${entry.path} 缺少 ${operation.method}`);
      assert.match(
        route.source,
        new RegExp(marker.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&'), 'u')
      );
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('Page')) {
      continue;
    }

    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`${pascal}`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
