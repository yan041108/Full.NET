import test from 'node:test';
import assert from 'node:assert/strict';
import { stripTypeScriptTypes } from 'node:module';
import { renderGeneratedFiles } from '../../scripts/openapi/generate-fullnet-client.mjs';

test('void-only Operations share dispatch and preserve undefined result, receiver and optional arguments', async () => {
 const operation = operationId => ({operationId,responses:{204:{description:'No Content'}}});
 const files = renderGeneratedFiles({openapi:'3.1.0',paths:{'/first':{post:operation('first')},'/second':{delete:operation('second')}}});
 // 只有 204 操作的文档也必须生成共享分派；不能依赖至少存在一个 JSON 响应。
 assert.equal(files['operations.generated.ts'].match(/options === undefined/gu)?.length,1);
 const code=stripTypeScriptTypes(files['operations.generated.ts']).replace(/^import[\s\S]*?;\s*/gmu,'');
 const generated=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
 const signal=new AbortController().signal,options={retryUnauthorized:false},calls=[];
 const http={request(...args){assert.equal(this,http);calls.push(args);return Promise.resolve('ignored custom body');}};
 assert.equal(await generated.first(http,{},signal),undefined);
 assert.equal(await generated.second(http,{},signal,options),undefined);
 assert.equal(calls[0].length,3);assert.equal(calls[1].length,4);assert.equal(calls[0][2],signal);assert.equal(calls[1][3],options);
 const failure=new Error('request failed');http.request=()=>Promise.reject(failure);
 await assert.rejects(generated.first(http,{}),error=>error===failure);
});

test('JSON Operations share dispatch while preserving options, receiver, cancellation and response guards', async () => {
 const schema = { type:'object', required:['name'], properties:{ name:{type:'string'} } };
 const operation = operationId => ({operationId,responses:{200:{description:'OK',content:{'application/json':{schema:{$ref:'#/components/schemas/Row'}}}}}});
 const files = renderGeneratedFiles({openapi:'3.1.0',paths:{'/first':{get:operation('first')},'/second':{get:operation('second')}},components:{schemas:{Row:schema}}});
 // 两个 JSON 操作应共享同一分派分支，避免每个 Operation 累积重复请求代码。
 assert.equal(files['operations.generated.ts'].match(/options === undefined/gu)?.length,1);
 const operations = stripTypeScriptTypes(files['operations.generated.ts']).replace(/^import[\s\S]*?;\s*/gmu,'');
 const guards = stripTypeScriptTypes(files['guards.generated.ts']);
 const generated = await import('data:text/javascript;base64,'+Buffer.from(guards+'\n'+operations).toString('base64'));
 const controller = new AbortController(); const calls = []; const options = {retryUnauthorized:false};
 let response = {name:'preserved'};
 const http = { request(...args) { assert.equal(this,http); calls.push(args); return Promise.resolve(response); } };
 assert.deepEqual(await generated.first(http,{},controller.signal),response);
 assert.deepEqual(await generated.second(http,{},controller.signal,options),response);
 assert.equal(calls[0].length,3); assert.equal(calls[1].length,4);
 assert.equal(calls[0][0],'/first'); assert.equal(calls[1][0],'/second');
 assert.equal(calls[0][2],controller.signal); assert.equal(calls[1][3],options);
 response = {name:123}; await assert.rejects(generated.first(http,{}),/invalid_row/u);
 const failure = new Error('request failed'); http.request = () => Promise.reject(failure);
 await assert.rejects(generated.first(http,{}),error => error === failure);
});
