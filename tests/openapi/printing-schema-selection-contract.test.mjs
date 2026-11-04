import test from 'node:test';import assert from 'node:assert/strict';import fs from 'node:fs';
import {validateVueClientContractCoverage} from '../../scripts/openapi/validate-vue-client-contract-coverage.mjs';
import {validateVueApiCallSiteCoverage} from '../../scripts/openapi/validate-vue-api-call-site-coverage.mjs';
test('打印业务表单选择保留现行服务端目录、记录资格和受保护生成操作',()=>{
 const value=JSON.parse(fs.readFileSync('contracts/openapi/fullnet-client-v1.openapi.json','utf8'));
 assert.equal(value.paths['/api/v1/printing/form-schemas'].get.operationId,'printingListFormSchemas');assert.ok(value.paths['/api/v1/printing/form-schemas'].get.security.length);
 assert.equal(value.components.schemas.PrintingFormSchemaDefinition.properties.requiresRecordId.type,'boolean');assert.equal(value.components.schemas.PrintingFormSchemaDefinition.properties.requiresRecordId.default,false);
});
test('打印表单目录适配器和实际页面/组件调用点完整登记',async()=>{
 const checks=await Promise.all([validateVueClientContractCoverage(),validateVueApiCallSiteCoverage()]);for(const result of checks)assert.deepEqual(result.filter(value=>/printing-templates|PrintingFormSchemaSelector|PrintingTemplateFields|PrintingPreviewView|PrintingPublishedTemplatesView/.test(value)),[]);
});
