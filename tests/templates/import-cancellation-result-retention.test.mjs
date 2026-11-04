import test from 'node:test';
import assert from 'node:assert/strict';

const load=()=>import('./support/application-import-cancellation-results.mjs');
const before=(statusKey='execution_partial')=>({id:'task',tenantId:'tenant',schemaKey:'organization.tenant_positions',worksheetKey:'positions',sourceFileId:'file',sourceFileName:'retention.xlsx',requestedByUserId:'actor',statusKey,totalRows:3,validRowCount:3,invalidRowCount:0,processedRowCount:3,succeededRowCount:statusKey==='execution_partial'?2:0,executionFailedRowCount:statusKey==='execution_partial'?1:3,nextLineNumber:3,hasErrorReceipt:true,errorCode:statusKey==='execution_failed'?'import_export.execution.failed':null,previewRows:[{lineNumber:1,isValid:true}],version:7,executionStartedAtUtc:'2026-10-14T00:00:00Z',executionCompletedAtUtc:'2026-10-14T00:01:00Z'});
const after=value=>({...structuredClone(value),statusKey:'cancelled',version:8,executionCompletedAtUtc:'2026-10-14T00:02:00Z'});

for(const status of ['execution_partial','execution_failed'])test(status+'取消保留原结果与回执身份',async()=>{
 const {assertCancelledResultRetained}=await load();const value=before(status);
 assert.doesNotThrow(()=>assertCancelledResultRetained(value,after(value)));
});
for(const key of Object.keys(before()).filter(key=>!['statusKey','version','executionCompletedAtUtc'].includes(key)))test('取消结果不得漂移 '+key,async()=>{
 const {assertCancelledResultRetained}=await load();const value=before(),changed=after(value);changed[key]='changed';
 assert.throws(()=>assertCancelledResultRetained(value,changed));
});
test('取消版本必须精确递增并接受长整型字符串',async()=>{
 const {assertCancelledResultRetained}=await load();const value={...before(),version:'9007199254740993'};
 assert.doesNotThrow(()=>assertCancelledResultRetained(value,{...after(value),version:'9007199254740994'}));
 for(const version of ['9007199254740995','broken',9007199254740992])assert.throws(()=>assertCancelledResultRetained(value,{...after(value),version}));
});
test('非取消终态、缺字段和无效完成时间不能通过',async()=>{
 const {assertCancelledResultRetained}=await load();const value=before();
 for(const change of [{statusKey:'execution_succeeded'},{executionCompletedAtUtc:null},{executionCompletedAtUtc:'invalid'},{version:7}])assert.throws(()=>assertCancelledResultRetained(value,{...after(value),...change}));
 const missing=after(value);delete missing.previewRows;assert.throws(()=>assertCancelledResultRetained(value,missing));
 assert.throws(()=>assertCancelledResultRetained({...value,statusKey:'executing'},after(value)));
});
test('回执下载必须保留有效原字节与文件名',async()=>{
 const {assertReceiptDownloadRetained}=await load();const value={bytes:new Uint8Array([1,2,3]),disposition:'attachment; filename="retention-errors.xlsx"'};
 assert.doesNotThrow(()=>assertReceiptDownloadRetained(value,{...value,bytes:new Uint8Array([1,2,3])}));
 for(const changed of [{...value,bytes:new Uint8Array([1,2,4])},{...value,disposition:'other'}, {bytes:new Uint8Array(),disposition:value.disposition}])assert.throws(()=>assertReceiptDownloadRetained(value,changed));
 assert.throws(()=>assertReceiptDownloadRetained({bytes:new Uint8Array(),disposition:null},{bytes:new Uint8Array(),disposition:null}));
});
