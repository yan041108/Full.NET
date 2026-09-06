import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const contractPath = path.join(
  repositoryRoot,
  'contracts/openapi/calendar-personal-schedules-v1.json'
);
const contractsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Calendar/Contracts/PersonalScheduleContracts.cs'
);
const permissionsSourcePath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Calendar/Contracts/CalendarPermissions.cs'
);
const endpointPath = path.join(
  repositoryRoot,
  'src/Modules/Full.NET.Modules.Calendar/Features/ManageMyPersonalSchedules/Endpoint.cs'
);

async function loadContract() {
  return JSON.parse(await readFile(contractPath, 'utf8'));
}

function isValidCalendarPermission(permission) {
  return /^calendar\.personal_schedules\.(read|create|update|delete|set_status)$/u.test(permission);
}

test('个人日程 OpenAPI 夹具结构完整且路径唯一', async () => {
  const contract = await loadContract();
  assert.equal(contract.id, 'calendar-personal-schedules-v1');

  const seen = new Set();
  for (const entry of contract.paths) {
    assert.match(entry.path, /^\/api\/v1\/calendar\//u);
    for (const operation of entry.operations) {
      const key = `${operation.method} ${entry.path}`;
      assert.ok(!seen.has(key), `重复操作：${key}`);
      seen.add(key);
      assert.ok(isValidCalendarPermission(operation.permission), `非法权限码：${operation.permission}`);
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

test('个人日程 OpenAPI 夹具与 C# 契约和端点源码一致', async () => {
  const contract = await loadContract();
  const contractsSource = await readFile(contractsSourcePath, 'utf8');
  const permissionsSource = await readFile(permissionsSourcePath, 'utf8');
  const endpointSource = await readFile(endpointPath, 'utf8');

  for (const permission of [
    'calendar.personal_schedules.read',
    'calendar.personal_schedules.create',
    'calendar.personal_schedules.update',
    'calendar.personal_schedules.delete',
    'calendar.personal_schedules.set_status'
  ]) {
    assert.ok(permissionsSource.includes(permission), `C# 契约缺少权限码：${permission}`);
  }

  assert.match(endpointSource, /MapGroup\("\/api\/v1\/calendar\/my-personal-schedules"\)/u);
  assert.match(endpointSource, /WithTags\("CalendarMyPersonalSchedules"\)/u);
  assert.match(endpointSource, /WithName\("calendarListMyPersonalSchedules"\)/u);
  assert.match(endpointSource, /WithName\("calendarGetMyPersonalSchedule"\)/u);
  assert.match(endpointSource, /WithName\("calendarCreateMyPersonalSchedule"\)/u);
  assert.match(endpointSource, /WithName\("calendarUpdateMyPersonalSchedule"\)/u);
  assert.match(endpointSource, /WithName\("calendarDeleteMyPersonalSchedule"\)/u);
  assert.match(endpointSource, /WithName\("calendarSetMyPersonalScheduleStatus"\)/u);

  const routeMarkers = new Map([
    ['GET /api/v1/calendar/my-personal-schedules', 'MapGet("/",'],
    ['POST /api/v1/calendar/my-personal-schedules', 'MapPost("/",'],
    ['GET /api/v1/calendar/my-personal-schedules/{scheduleId}', 'MapGet("/{scheduleId:guid}",'],
    ['PUT /api/v1/calendar/my-personal-schedules/{scheduleId}', 'MapPut("/{scheduleId:guid}",'],
    [
      'POST /api/v1/calendar/my-personal-schedules/{scheduleId}/delete',
      'MapPost("/{scheduleId:guid}/delete",'
    ],
    [
      'POST /api/v1/calendar/my-personal-schedules/{scheduleId}/status',
      'MapPost("/{scheduleId:guid}/status",'
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
