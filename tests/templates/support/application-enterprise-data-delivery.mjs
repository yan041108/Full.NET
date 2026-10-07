import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { writeFileSync } from 'node:fs';
import { setTimeout as sleep } from 'node:timers/promises';

// 两种非空工作簿先预览并排队，再启动独立 Worker；只能从正式业务 API 验证写入结果。
export async function verifyEnterpriseDataDeliveryHttp(baseUrl, { hostAccessToken, logPath, fillWorkbook, startWorker, signal, request = fetch }) {
 assert.ok(typeof hostAccessToken === 'string' && hostAccessToken.trim(), 'Host session is required');
 assert.equal(typeof startWorker, 'function');
 const evidence = { completed: false, responses: [], tasks: [] };
 const secrets = [hostAccessToken];
 const redact = text => secrets.reduce((value, secret) => value.replaceAll(secret, '[REDACTED]'), String(text));
 let tenantToken;
 const send = async (stage, path, method, body, status = 200, token = tenantToken ?? hostAccessToken, binary = false) => {
  const headers = { Authorization: `Bearer ${token}`, Origin: 'http://localhost' };
  if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';
  const response = await request(baseUrl + path, { method, headers, body: body === undefined || body instanceof FormData ? body : JSON.stringify(body),
   redirect: 'error', signal: signal ? AbortSignal.any([signal,AbortSignal.timeout(15_000)]) : AbortSignal.timeout(15_000) });
  evidence.responses.push({ stage, status: response.status });
  // 不读取失败正文到错误或报告，签发响应与第三方错误都可能回显未知凭据。
  assert.equal(response.status, status, `${stage}: unexpected HTTP ${response.status}`);
  if (binary) return new Uint8Array(await response.arrayBuffer());
  try { return await response.json(); } catch { throw new Error(stage + ': invalid JSON response'); }
 };
 const assertId = id => assert.match(id ?? '', /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu);
 try {
  const available = await send('available', '/api/v1/tenancy/available', 'GET');
  assert.ok(Array.isArray(available));
  const tenants = available.filter(item => item.identifier === 'local'); assert.equal(tenants.length, 1);
  const tenantId = tenants[0].id; assertId(tenantId);
  const switched = await send('context', '/api/v1/tenancy/context', 'PUT', { tenantId });
  assert.ok(typeof switched.accessToken === 'string' && switched.accessToken.trim(), 'tenant session is missing');
  tenantToken = switched.accessToken; secrets.push(tenantToken);
  assert.equal(switched.context?.tenantId, tenantId, 'context selected a different tenant');
  const me = await send('actor', '/api/v1/me', 'GET'); assertId(me.id);
  const unitCode = 'delivery-unit-' + randomUUID().replaceAll('-','');
  const levelCode = 'delivery-level-' + randomUUID().replaceAll('-','');
  const unit = await send('unit', '/api/v1/organization/units', 'POST', { parentId: null, code: unitCode, name: 'Enterprise Worker unit', displayOrder: 10 }, 201);
  const level = await send('level', '/api/v1/organization/position-levels', 'POST', { code: levelCode, name: 'Enterprise Worker level', displayOrder: 10 }, 201);
  assertId(unit.id); assertId(level.id);
  await send('membership', '/api/v1/organization/user-units', 'POST', { userId: me.id, unitId: unit.id, isPrimary: false }, 201);
  const positionCode = 'delivery-position-' + randomUUID().replaceAll('-','');
  const requestNumber = 'DELIVERY-' + randomUUID().replaceAll('-','');
  const definitions = [
   { schema: 'organization.tenant_positions', worksheet: 'positions', values: [positionCode,'Enterprise Worker position','10',unitCode,levelCode], line: 1 },
   { schema: 'demo.enterprise_requests', worksheet: 'requests', values: [requestNumber,'Enterprise Worker request','123.45',me.id,unit.id], line: 2 },
  ];
  for (const definition of definitions) {
   const template = await send('template-' + definition.schema, `/api/v1/import-export/schemas/${definition.schema}/worksheets/${definition.worksheet}/template`, 'GET', undefined, 200, tenantToken, true);
   assert.equal(template[0], 80); assert.equal(template[1], 75);
   const workbook = await fillWorkbook(template, definition.values);
   const form = new FormData(); form.set('schemaKey',definition.schema); form.set('worksheetKey',definition.worksheet);
   form.set('file', new Blob([workbook], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }), 'delivery.xlsx');
   const task = await send('preview-' + definition.schema, '/api/v1/import-export/tasks', 'POST', form, 201);
   assertId(task.id); assert.equal(task.tenantId,tenantId, 'task tenant mismatch'); assert.equal(task.schemaKey,definition.schema);
   assert.equal(task.statusKey,'preview_succeeded'); assert.equal(task.totalRows,1); assert.equal(task.validRowCount,1); assert.equal(task.invalidRowCount,0);
   assert.equal(task.previewRows?.length,1); assert.equal(task.previewRows[0].lineNumber,definition.line); assert.equal(task.previewRows[0].isValid,true);
   const queued = await send('queue-' + definition.schema, `/api/v1/import-export/tasks/${task.id}/execute`, 'POST');
   assert.equal(queued.statusKey,'queued', 'API must queue without a running Worker');
   evidence.tasks.push({ id:task.id, schema:definition.schema, queued:true, completed:false });
  }
  await startWorker(); evidence.workerStartedAfterQueue = true;
  for (const task of evidence.tasks) {
   const deadline = Date.now() + 90_000;
   while (true) {
    const current = await send('poll-' + task.schema, `/api/v1/import-export/tasks/${task.id}`, 'GET');
    assert.equal(current.id,task.id); assert.equal(current.tenantId,tenantId, 'completed task tenant mismatch');
    if (current.statusKey === 'execution_succeeded') {
     assert.equal(current.processedRowCount,1); assert.equal(current.succeededRowCount,1); assert.equal(current.nextLineNumber,1); assert.equal(current.executionFailedRowCount,0);
     task.completed = true; break;
    }
    assert.ok(['queued','executing'].includes(current.statusKey), `unexpected task terminal state: ${current.statusKey}`);
    assert.ok(Date.now() < deadline, 'Worker execution timeout');
    await sleep(250,undefined,{signal});
   }
  }
  const positions = await send('position-list','/api/v1/organization/positions?page=1&pageSize=100','GET');
  const positionMatches = positions.items?.filter(item => item.code === positionCode); assert.equal(positionMatches?.length,1,'position must be created once');
  const position = await send('position-read',`/api/v1/organization/positions/${positionMatches[0].id}`,'GET');
  assert.equal(position.id,positionMatches[0].id); assert.equal(position.unitId,unit.id); assert.equal(position.positionLevelId,level.id);
  assert.equal(position.name,'Enterprise Worker position');
  const applications = await send('request-list','/api/v1/enterprise_request/enterprise-requests?page=1&pageSize=100','GET');
  const applicationMatches = applications.items?.filter(item => item.requestNumber === requestNumber); assert.equal(applicationMatches?.length,1,'request must be created once');
  const application = await send('request-read',`/api/v1/enterprise_request/enterprise-requests/${applicationMatches[0].id}`,'GET');
  assert.equal(application.id,applicationMatches[0].id); assert.equal(application.tenantId,tenantId,'business row tenant mismatch');
  assert.equal(application.organizationUnitId,unit.id); assert.equal(application.requestNumber,requestNumber); assert.equal(application.title,'Enterprise Worker request');
  assert.equal(application.totalAmount,123.45); assert.equal(application.applicantUserId,me.id); assert.equal(application.status,'draft');
  evidence.completed = true; evidence.tenantId = tenantId; evidence.positionId = position.id; evidence.requestId = application.id;
  return evidence;
 } catch (error) {
  evidence.error = redact(error instanceof Error ? error.message : error);
  throw new Error(evidence.error);
 } finally { writeFileSync(logPath,redact(JSON.stringify(evidence,null,2))); }
}
