import assert from 'node:assert/strict';import fs from 'node:fs/promises';import test from 'node:test';
test('所有者模块契约覆盖独立读取、本人回执和交接写入，回执无验证凭据',async()=>{
 const contract=JSON.parse(await fs.readFile(new URL('../../contracts/openapi/tenant-ownership-v1.json',import.meta.url),'utf8'));
 const operations=Object.values(contract.paths).flatMap(item=>Object.values(item).map(operation=>operation.operationId));
 assert.deepEqual(operations,['identityGetTenantOwnership','identityGetTenantOwnerTransferReceipt','identityTransferTenantOwner']);
 const receipt=contract.components.schemas.TenantOwnerTransferReceipt.properties;
 assert.equal(receipt.currentPassword,undefined);assert.equal(receipt.totpCode,undefined);assert.equal(receipt.requestDigest,undefined);
 assert.equal(contract.components.schemas.TransferTenantOwnerRequest.properties.currentPassword.writeOnly,true);
});
test('所有者适配器、共享契约和真实弹窗的调用点精确登记',async()=>{
 const {validateVueClientContractCoverage}=await import('../../scripts/openapi/validate-vue-client-contract-coverage.mjs');
 const {validateVueApiCallSiteCoverage}=await import('../../scripts/openapi/validate-vue-api-call-site-coverage.mjs');
 const violations=[...await validateVueClientContractCoverage(),...await validateVueApiCallSiteCoverage()];
 assert.deepEqual(violations.filter(value=>value.includes('tenant-ownership')||value.includes('TenantOwnerTransferDialog')||value.includes('tenant-members.ts')),[]);
});
