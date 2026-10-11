import assert from 'node:assert/strict';
import test from 'node:test';
import { compareContractSets } from '../../scripts/openapi/openapi-contract-compatibility.mjs';

const file='fullnet-client-v1.openapi.json';
const names=['HostDocumentVersionRetentionSettingsResponse','UpdateHostDocumentVersionRetentionRequest'];
const fields=['minimumRetainedVersionsPerItem','maximumRetainedHistoryVersions','pollSeconds','batchSize'];
function baseline() {
  const response={description:'OK',content:{'application/json':{schema:{$ref:'#/components/schemas/'+names[0]}}}};
  return {openapi:'3.1.0',paths:{'/retention':{
    get:{security:[{Bearer:[]}],responses:{'200':response}},
    put:{security:[{Bearer:[]}],requestBody:{required:true,content:{'application/json':{schema:{$ref:'#/components/schemas/'+names[1]}}}},responses:{'200':response}}
  }},components:{schemas:Object.fromEntries(names.map(name=>[name,{type:'object',properties:Object.fromEntries(fields.map(field=>[field,{type:'integer',format:'int32'}])),required:[...fields]}]))}};
}
function evolved() {
  const value=baseline();
  for(const schema of Object.values(value.components.schemas)) schema.properties.version={type:['integer','string'],format:'int64',pattern:'^-?(?:0|[1-9]\\d*)$'};
  value.components.schemas[names[0]].required.push('version');
  return value;
}
const compare=value=>compareContractSets(new Map([[file,baseline()]]),new Map([[file,value]]));

test('保留策略只新增请求可选版本和响应必返版本时兼容',()=>assert.deepEqual(compare(evolved()),[]));
for(const [label,mutate]of [
  ['请求版本改为必填',value=>value.components.schemas[names[1]].required.push('version')],
  ['改写旧请求字段',value=>value.components.schemas[names[1]].properties.batchSize.type='string'],
  ['改写旧响应字段',value=>value.components.schemas[names[0]].properties.batchSize.type='string'],
  ['删除旧响应字段',value=>delete value.components.schemas[names[0]].properties.batchSize],
  ['删除旧请求字段',value=>delete value.components.schemas[names[1]].properties.batchSize],
  ['取消旧响应必返性',value=>{value.components.schemas[names[0]].required=value.components.schemas[names[0]].required.filter(field=>field!=='batchSize');}],
  ['改写请求对象边界',value=>value.components.schemas[names[1]].additionalProperties=false],
  ['移除认证',value=>value.paths['/retention'].put.security=[]],
  ['改写成功响应',value=>value.paths['/retention'].put.responses['200'].description='changed']
]) test('保留策略版本扩展仍拒绝'+label,()=>{const value=evolved();mutate(value);assert.ok(compare(value).length>0);});
