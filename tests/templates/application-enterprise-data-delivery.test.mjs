import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { verifyEnterpriseDataDeliveryHttp } from './support/application-enterprise-data-delivery.mjs';

const ids = { tenant: '01980000-0000-7000-8000-000000000001', user: '01980000-0000-7000-8000-000000000002',
 unit: '01980000-0000-7000-8000-000000000003', level: '01980000-0000-7000-8000-000000000004' };

async function fixture({ wrongStatus = false, wrongTenant = false } = {}) {
 const directory = mkdtempSync(join(tmpdir(), 'enterprise-http-contract-'));
 const logPath = join(directory, 'report.json');
 let started = false;
 const tasks = [];
 const fills = [];
 const request = async (url, options) => {
  const path = new URL(url).pathname;
  let status = 200;
  let body;
  const input = typeof options.body === 'string' ? JSON.parse(options.body) : undefined;
  if (path.endsWith('/tenancy/available')) body = [{ id: ids.tenant, identifier: 'local' }];
  else if (path.endsWith('/tenancy/context')) body = { accessToken: 'TENANT_SECRET', context: { tenantId: ids.tenant } };
  else if (path.endsWith('/me')) body = { id: ids.user };
  else if (path.endsWith('/organization/units')) { status = 201; body = { id: ids.unit, code: input.code }; }
  else if (path.endsWith('/organization/position-levels')) { status = 201; body = { id: ids.level, code: input.code }; }
  else if (path.endsWith('/organization/user-units')) { status = 201; body = { id: ids.unit }; }
  else if (path.endsWith('/template')) return new Response(new Uint8Array([80,75]), { status: 200 });
  else if (path.endsWith('/import-export/tasks') && options.method === 'POST') {
   assert.equal(started, false, 'tasks must preview before Worker starts');
   const schemaKey = options.body.get('schemaKey');
   const id = `01980000-0000-7000-8000-${String(tasks.length + 10).padStart(12,'0')}`;
   body = { id, schemaKey, tenantId: ids.tenant, statusKey: 'preview_succeeded', totalRows: 1, validRowCount: 1, invalidRowCount: 0, previewRows: [{lineNumber: schemaKey.startsWith('demo.') ? 2 : 1, isValid:true}] };
   tasks.push(body); status = wrongStatus ? 200 : 201;
  } else if (path.endsWith('/execute')) {
   assert.equal(started, false, 'execution must queue while Worker is stopped');
   body = { ...tasks.find(t => path.includes(t.id)), statusKey: 'queued' };
  } else if (path.includes('/import-export/tasks/')) {
   assert.equal(started, true);
   const task = tasks.find(t => path.endsWith(t.id));
   body = { ...task, statusKey: 'execution_succeeded', succeededRowCount: 1, nextLineNumber: 1,
    processedRowCount: 1, executionFailedRowCount: 0, executionRows: [{ lineNumber: task.schemaKey.startsWith('demo.') ? 2 : 1,
    succeeded: true, entityId: task.id }] };
  } else if (path.endsWith('/organization/positions')) body = {items:[{id:tasks[0].id,code:fills[0][0]}]};
  else if (path.includes('/organization/positions/')) {
   body = { id: tasks[0].id, unitId: ids.unit, positionLevelId: ids.level, name: 'Enterprise Worker position' };
  } else if (path.includes('/enterprise_request/enterprise-requests/')) {
   body = { id: tasks[1].id, tenantId: wrongTenant ? ids.user : ids.tenant, organizationUnitId: ids.unit,
    requestNumber: fills[1][0], title: 'Enterprise Worker request', totalAmount: 123.45,
    applicantUserId: ids.user, status: 'draft' };
  } else if (path.endsWith('/enterprise_request/enterprise-requests')) body = { items: [{ id: tasks[1].id, requestNumber: fills[1][0] }] };
  else throw new Error('Unexpected request path: ' + path);
  return Response.json(body, { status });
 };
 const run = () => verifyEnterpriseDataDeliveryHttp('http://localhost:1234', { hostAccessToken: 'HOST_SECRET', logPath, request,
  fillWorkbook: (template, values) => { assert.deepEqual([...template], [80,75]); fills.push(values); return template; },
  startWorker: async () => { assert.equal(tasks.length, 2); started = true; } });
 return { run, report: () => readFileSync(logPath,'utf8'), cleanup: () => rmSync(directory,{recursive:true,force:true}) };
}

test('enterprise delivery verifies two nonempty queued schemas and domain fields after real Worker handoff', async () => {
 const f = await fixture();
 try {
  const result = await f.run();
  assert.equal(result.completed, true); assert.equal(result.tasks.length, 2);
  assert.doesNotMatch(f.report(), /HOST_SECRET|TENANT_SECRET/u);
 } finally { f.cleanup(); }
});

test('enterprise delivery rejects mismatched create HTTP status and saves incomplete evidence', async () => {
 const f = await fixture({wrongStatus:true});
 try { await assert.rejects(f.run(), /HTTP 200/u); assert.equal(JSON.parse(f.report()).completed,false); }
 finally { f.cleanup(); }
});

test('enterprise delivery rejects a business row belonging to a different tenant', async () => {
 const f = await fixture({wrongTenant:true});
 try { await assert.rejects(f.run(), /tenant/u); assert.equal(JSON.parse(f.report()).completed,false); }
 finally { f.cleanup(); }
});
