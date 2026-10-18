import assert from 'node:assert/strict';
import {afterEach,beforeEach,test} from 'node:test';
import {createRequire} from 'node:module';
import {configureRealStackContainer} from '../e2e/admin-real-stack/scripts/container-resource-profile.mjs';
const require=createRequire(new URL('../e2e/admin-real-stack/package.json', import.meta.url));
const {GenericContainer}=require('testcontainers');
let previous;
beforeEach(()=>{previous=process.env.FULLNET_E2E_RESOURCE_PROFILE;process.env.FULLNET_E2E_RESOURCE_PROFILE='bounded-local';});
afterEach(()=>{if(previous===undefined)delete process.env.FULLNET_E2E_RESOURCE_PROFILE;else process.env.FULLNET_E2E_RESOURCE_PROFILE=previous;});
for(const [role,memory,cpu] of [['sqlserver',3,1],['mysql',1.5,1],['redis',0.25,0.25]]){
 test(`real stack ${role} configures actual SDK quotas before Docker create`,()=>{
  const container=new GenericContainer('redis:8.6');
  assert.equal(configureRealStackContainer(container,role),container);
  assert.equal(container.hostConfig.Memory,memory*1024**3);
  assert.equal(container.hostConfig.NanoCpus,cpu*1e9);
 });
}
test('an absent bounded profile preserves default container configuration',()=>{
 delete process.env.FULLNET_E2E_RESOURCE_PROFILE;
 const container=configureRealStackContainer(new GenericContainer('redis:8.6'),'redis');
 assert.equal(container.hostConfig.Memory,undefined);
 assert.equal(container.hostConfig.NanoCpus,undefined);
});
test('an unknown profile refuses configuration',()=>{
 process.env.FULLNET_E2E_RESOURCE_PROFILE='bounded-lcoal';
 assert.throws(()=>configureRealStackContainer(new GenericContainer('redis:8.6'),'redis'),/resource profile/u);
});
test('an unknown or inherited role cannot silently start without quotas',()=>{
 for(const role of ['future','__proto__','toString'])assert.throws(()=>configureRealStackContainer(new GenericContainer('redis:8.6'),role),/resource role/u);
});
