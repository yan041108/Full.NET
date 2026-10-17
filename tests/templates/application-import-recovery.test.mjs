import test from 'node:test';
import assert from 'node:assert/strict';
import { assertImportProgress, assertReceiptIdentity } from './support/application-import-recovery.mjs';

const expected={status:'execution_partial',next:3,succeeded:2,failed:1,receipt:true};
const task={statusKey:expected.status,nextLineNumber:3,processedRowCount:3,succeededRowCount:2,executionFailedRowCount:1,hasErrorReceipt:true};
const first='01980000-0000-7000-8000-000000000001',second='01980000-0000-7000-8000-000000000002';

test('多行完成结果逐项验证检查点和计数',()=>assert.doesNotThrow(()=>assertImportProgress(task,expected)));
for(const[key,value]of Object.entries({statusKey:'execution_succeeded',nextLineNumber:2,processedRowCount:4,succeededRowCount:3,executionFailedRowCount:0,hasErrorReceipt:false})){
 test(`拒绝错误多行结果 ${key}`,()=>assert.throws(()=>assertImportProgress({...task,[key]:value},expected)));
}
test('同结果恢复复用回执，不同结果重试使用新回执',()=>{
 assertReceiptIdentity(first,first,true);assertReceiptIdentity(first,second,false);
});
for(const[before,after,reused,label]of [[first,second,true,'重复上传'],[first,first,false,'误用旧回执'],[null,first,true,'缺少原回执'],[first,'bad',false,'非法新回执']]){
 test(`拒绝回执身份错误 ${label}`,()=>assert.throws(()=>assertReceiptIdentity(before,after,reused)));
}
