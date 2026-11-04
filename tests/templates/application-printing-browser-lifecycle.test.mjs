import assert from 'node:assert/strict';
import test from 'node:test';
import { watchPrintingBrowserCancellation, runPrintingBrowserResponseAction } from './support/application-printing-browser-lifecycle.mjs';

test('取消必须关闭自有浏览器并中断挂起页面步骤',async()=>{
 const controller=new AbortController();let rejectPending;let closed=0;
 const pendingPage=new Promise((_,reject)=>{rejectPending=reject;});
 const scope=watchPrintingBrowserCancellation(controller.signal,async()=>{closed++;rejectPending(new Error('owned context closed'));});
 const interrupted=assert.rejects(pendingPage,/owned context closed/u);
 controller.abort();await interrupted;await scope.dispose();assert.equal(closed,1);
});
test('已取消的验收不能开始新的浏览器工作',()=>{
 const controller=new AbortController();controller.abort();
 assert.throws(()=>watchPrintingBrowserCancellation(controller.signal,()=>assert.fail('must not start')),{name:'AbortError'});
});
test('完成后解除监听，关闭失败仍必须传播到清理报告',async()=>{
 const controller=new AbortController();let closed=0;
 const scope=watchPrintingBrowserCancellation(controller.signal,()=>{closed++;});
 await scope.dispose();controller.abort();await Promise.resolve();assert.equal(closed,0);
 const failing=new AbortController();const failed=watchPrintingBrowserCancellation(failing.signal,async()=>{throw new Error('cleanup failed');});
 failing.abort();await assert.rejects(failed.dispose(),/cleanup failed/u);
});

test('取消同时拒绝点击与响应等待时，两个拒绝都必须被接管',async()=>{
 const controller=new AbortController();let rejectResponse;let rejectClick;let entered;
 const ready=new Promise(resolve=>{entered=resolve;});
 const response=new Promise((_,reject)=>{rejectResponse=reject;});
 const click=new Promise((_,reject)=>{rejectClick=reject;});
 const scope=watchPrintingBrowserCancellation(controller.signal,async()=>{rejectResponse(new Error('response closed'));rejectClick(new Error('click closed'));});
 const waiting=runPrintingBrowserResponseAction({waitForResponse:()=>response},()=>true,()=>{entered();return click;});
 const assertion=assert.rejects(waiting,/closed/u);await ready;controller.abort();await assertion;await scope.dispose();
 // Node test runner 会把未处理拒绝直接判为失败；跨一个事件循环确认晚到拒绝也已处理。
 await new Promise(resolve=>setImmediate(resolve));
});
