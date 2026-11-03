import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const api = JSON.parse(readFileSync(new URL('../../contracts/openapi/fullnet-client-v1.openapi.json', import.meta.url)));
const operations = Object.values(api.paths).flatMap(path => Object.values(path)).filter(x => x.operationId?.startsWith('enterpriseRequest'));
const byId = id => operations.find(x => x.operationId === `enterpriseRequest${id}`);
const resolve = schema => schema?.$ref ? api.components.schemas[schema.$ref.split('/').at(-1)] : schema;

test('交付接口公开九个实际能力并保持 multipart 和无损版本线格式', () => {
  for (const name of ['ListAttachments', 'UploadAttachment', 'DeleteAttachment', 'DownloadAttachment', 'ListReports',
    'CreateReport', 'CancelReport', 'DownloadReport', 'PrintDocument']) assert.ok(byId(name), name);
  const upload = byId('UploadAttachment');
  assert.ok(upload.requestBody.content['multipart/form-data']);
  assert.equal(upload.parameters.find(p => p.name === 'requestVersion').schema.type, 'string');
  for (const [name, field] of [['CreateReport', 'requestVersion'], ['DeleteAttachment', 'attachmentVersion'], ['CancelReport', 'version']]) {
    const schema = resolve(byId(name).requestBody.content['application/json'].schema);
    assert.equal(resolve(schema.properties[field]).type, 'string');
    for (const forbidden of ['tenantId', 'userId', 'sessionId', 'includesAmounts', 'sourceDigest', 'fileId'])
      assert.equal(Object.hasOwn(schema.properties, forbidden), false);
  }
});

test('文件返回二进制而不是暴露存储位置或下载外链', () => {
  for (const name of ['DownloadAttachment', 'DownloadReport']) {
    const content = byId(name).responses['200'].content;
    assert.ok(Object.keys(content).some(key => key === 'application/octet-stream' || key.includes('spreadsheetml')));
  }
  const response = resolve(byId('UploadAttachment').responses['200'].content['application/json'].schema);
  for (const field of ['storageKey', 'objectKey', 'url', 'fileId', 'tenantId', 'securityStamp']) assert.equal(Object.hasOwn(response.properties, field), false);
});
