import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/platform-release-notes-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Platform/Contracts/ReleaseNoteContracts.cs'
);
const permissionsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Platform/Contracts/PlatformPermissions.cs'
);
const hostEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Platform/Features/ManageHostReleaseNotes/Endpoint.cs'
);
const myEndpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Platform/Features/ManageMyReleaseNotes/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

function isValidPlatformPermission(permission) {
  return /^platform\.release_notes\.(read|create|update|publish|retract|delete|mark_read)$/u.test(
    permission
  );
}

test('平台更新日志 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'platform-release-notes-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/platform\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok(isValidPlatformPermission(operation.permission), `非法权限码：${operation.permission}`);
      if (operation.requestSchema) {
        assert.ok(contract.schemas[operation.requestSchema]);
      }
      if (operation.successStatus === 204) {
        assert.equal(operation.responseSchema, undefined);
      } else {
        assert.ok(contract.schemas[operation.responseSchema]);
      }
    }
  }
});

test('平台更新日志 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const permissionsSource = await readFile(permissionsSourcePath, 'utf8');
  const hostEndpointSource = await readFile(hostEndpointPath, 'utf8');
  const myEndpointSource = await readFile(myEndpointPath, 'utf8');
  const endpointSource = `${hostEndpointSource}\n${myEndpointSource}`;

  for (const permission of [
    'platform.release_notes.read',
    'platform.host_release_notes.read',
    'platform.release_notes.create',
    'platform.release_notes.update',
    'platform.release_notes.publish',
    'platform.release_notes.retract',
    'platform.release_notes.delete',
    'platform.release_notes.mark_read'
  ]) {
    assert.ok(permissionsSource.includes(permission), `C# 契约缺少权限码：${permission}`);
  }

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/platform\/host-release-notes"\)/u);
  assert.match(endpointSource, /MapGroup\("\/api\/v1\/platform\/my-release-notes"\)/u);
  assert.match(endpointSource, /WithTags\("PlatformHostReleaseNotes"\)/u);
  assert.match(endpointSource, /WithTags\("PlatformMyReleaseNotes"\)/u);
  assert.match(endpointSource, /WithName\("platformListHostReleaseNotes"\)/u);
  assert.match(endpointSource, /WithName\("platformGetHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformCreateHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformUpdateHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformPublishHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformRetractHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformDeleteHostReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformListMyReleaseNotes"\)/u);
  assert.match(endpointSource, /WithName\("platformGetLatestUnreadReleaseNote"\)/u);
  assert.match(endpointSource, /WithName\("platformMarkMyReleaseNoteRead"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/platform/host-release-notes', 'MapGet("/",'],
    ['POST /api/v1/platform/host-release-notes', 'MapPost("/",'],
    ['GET /api/v1/platform/host-release-notes/{releaseNoteId}', 'MapGet("/{releaseNoteId:guid}",'],
    ['PUT /api/v1/platform/host-release-notes/{releaseNoteId}', 'MapPut("/{releaseNoteId:guid}",'],
    [
      'POST /api/v1/platform/host-release-notes/{releaseNoteId}/publish',
      'MapPost("/{releaseNoteId:guid}/publish",'
    ],
    [
      'POST /api/v1/platform/host-release-notes/{releaseNoteId}/retract',
      'MapPost("/{releaseNoteId:guid}/retract",'
    ],
    [
      'POST /api/v1/platform/host-release-notes/{releaseNoteId}/delete',
      'MapPost("/{releaseNoteId:guid}/delete",'
    ],
    ['GET /api/v1/platform/my-release-notes', 'MapGet("/",'],
    ['GET /api/v1/platform/my-release-notes/latest-unread', 'MapGet("/latest-unread",'],
    [
      'POST /api/v1/platform/my-release-notes/{releaseNoteId}/read',
      'MapPost("/{releaseNoteId:guid}/read",'
    ]
  ]);

  for (const entry of contract.paths) {
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      const marker = routeMarkers.get(key);
      assert.ok(marker, `未登记的路由操作：${key}`);
      assert.ok(endpointSource.includes(marker), `端点源码缺少：${key}`);
    }
  }

  for (const [schemaName, schema] of Object.entries(contract.schemas)) {
    if (schemaName.endsWith('Page')) {
      continue;
    }
    assert.match(contractsSource, new RegExp(`record ${schemaName}\\b`, 'u'));
    for (const property of schema.properties) {
      const pascal = property.charAt(0).toUpperCase() + property.slice(1);
      assert.match(
        contractsSource,
        new RegExp(`\\b${pascal}\\b`, 'u'),
        `${schemaName}.${property} 未在 C# 契约中找到`
      );
    }
  }
});
