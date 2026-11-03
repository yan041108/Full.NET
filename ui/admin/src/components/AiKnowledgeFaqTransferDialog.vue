<script setup lang="ts">
import {computed,onUnmounted,ref,watch} from 'vue';
import {ElAlert,ElButton,ElCheckbox,ElCheckboxGroup,ElFormItem,ElInput} from 'element-plus';
import {isFullNetProblemDetails,validateKnowledgeFaqTransferPackage,type AiKnowledgeFaqDraftWorkspace,type AiKnowledgeFaqTransferPackage,type AiKnowledgeFaqImportReport} from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeFaqTransferText} from '../i18n/ai-knowledge-faq-transfer';
import {showSuccess,showWarning,showError} from '../feedback/fullNetMessage';
import * as api from '../api/ai-knowledge-faq-transfer';
const props=defineProps<{open:boolean;knowledgeBaseId:string;mode:'import'|'export';rows:AiKnowledgeFaqDraftWorkspace[]}>();
const emit=defineEmits<{'update:open':[boolean];completed:[];unavailable:[];openFaq:[string]}>();
const session=useSessionStore();const {locale}=useAdminI18n();const tr=(key:Parameters<typeof knowledgeFaqTransferText>[1])=>knowledgeFaqTransferText(locale.value,key);
const text=ref('');const selected=ref<string[]>([]);const busy=ref(false);const report=ref<AiKnowledgeFaqImportReport>();const exported=ref(false);
const allowed=computed(()=>!session.switching&&session.can('ai.knowledge_bases.read')&&session.can('ai.knowledge_faqs.read')&&session.can(props.mode==='export'?'ai.knowledge_faqs.export':'ai.knowledge_faqs.import')&&(props.mode==='export'||session.can('ai.knowledge_faqs.write')));
let epoch=0;let controller:AbortController|undefined;let pending:{package:AiKnowledgeFaqTransferPackage;digest:string;keys:string[]}|undefined;
function stop(){++epoch;controller?.abort();busy.value=false;}
function clear(){stop();text.value='';selected.value=[];report.value=undefined;exported.value=false;pending=undefined;}
function close(){clear();emit('update:open',false);}
function changed(){stop();report.value=undefined;exported.value=false;pending=undefined;}
async function readFile(event:Event){const input=event.target as HTMLInputElement;const file=input.files?.[0];if(!file||busy.value||!allowed.value)return;changed();text.value='';const current=++epoch;
  try{if(file.size>512*1024)throw Error();const value=await file.text();if(current!==epoch||!props.open||!allowed.value)return;validateKnowledgeFaqTransferPackage(JSON.parse(value));text.value=value;}catch{if(current===epoch)showWarning(tr('invalid'));}finally{input.value='';}}
// 请求键只在新的已核证导入意图产生；未知响应保留全部原键供明确重试。
function key(){const b=crypto.getRandomValues(new Uint8Array(16));let n=BigInt(Date.now());for(let i=5;i>=0;i--){b[i]=Number(n&255n);n>>=8n;}b[6]=(b[6]!&15)|112;b[8]=(b[8]!&63)|128;const h=Array.from(b,v=>v.toString(16).padStart(2,'0')).join('');return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`;}
async function run(action:(signal:AbortSignal)=>Promise<void>){if(busy.value||!props.open||!allowed.value||!props.knowledgeBaseId)return;stop();const current=++epoch;controller=new AbortController();busy.value=true;
  try{await action(controller.signal);}catch(error){if(current!==epoch)return;if(isFullNetProblemDetails(error)&&[401,403,404,409].includes(error.status)){clear();showError(tr('unavailable'));emit('unavailable');}else showWarning(tr('failed'));}finally{if(current===epoch)busy.value=false;}}
async function preview(){let pack:AiKnowledgeFaqTransferPackage;try{if(new TextEncoder().encode(text.value).length>512*1024)throw Error();pack=validateKnowledgeFaqTransferPackage(JSON.parse(text.value));}catch{changed();showWarning(tr('invalid'));return;}
  await run(async signal=>{const value=await api.previewFaqImport({knowledgeBaseId:props.knowledgeBaseId,body:{package:pack}},signal);if(signal.aborted)return;report.value=value;
    if(value.items.some(item=>item.status!=='ready')){text.value='';pending=undefined;const denied=value.items.some(item=>item.status==='classification_conflict');showWarning(tr(denied?'classification_conflict':'unavailable'));if(!denied)emit('unavailable');return;}
    pending={package:pack,digest:value.digest,keys:pack.items.map(()=>key())};});}
async function commit(){const request=pending;if(!request||!report.value?.items.every(item=>item.status==='ready'))return;
  await run(async signal=>{const value=await api.importFaqs({knowledgeBaseId:props.knowledgeBaseId,body:{package:request.package,expectedDigest:request.digest,requestKeys:request.keys}},signal);if(signal.aborted)return;report.value=value;text.value='';pending=undefined;
    if(value.items.some(item=>!['created','replayed'].includes(item.status))){showWarning(tr('completed'));emit('unavailable');}else showSuccess(tr('completed'));emit('completed');});}
async function readExport(){if(selected.value.length<1||selected.value.length>8)return;exported.value=false;text.value='';await run(async signal=>{const pack=await api.exportFaqs({knowledgeBaseId:props.knowledgeBaseId,body:{faqIds:[...selected.value]}},signal);if(signal.aborted)return;text.value=JSON.stringify(pack);exported.value=true;});}
async function download(){if(!exported.value||!allowed.value)return;await readExport();if(!exported.value||!allowed.value)return;const url=URL.createObjectURL(new Blob([text.value],{type:'application/json'}));const a=document.createElement('a');a.href=url;a.download='fullnet-faq-transfer.json';a.click();URL.revokeObjectURL(url);}
watch(()=>[props.open,props.knowledgeBaseId,props.mode],clear);watch(()=>[session.currentUser,session.switching,allowed.value],()=>{clear();if(props.open&&!allowed.value)emit('update:open',false);},{deep:true});onUnmounted(clear);
</script>
<template>
  <!-- 保留弹层自己的 section 语义，避免第三方标题 header 被识别为第二个页面 banner。 -->
  <Teleport to="body"><section :aria-label="tr('title')"><ArtFormDialog :open="open" :title="tr('title')" width="760px" :show-confirm="false" :cancel-label="tr('close')" :trap-focus="true" :append-to-body="false" class="faq-transfer-dialog" data-testid="faq-transfer-dialog" @update:open="close">
  <ElAlert :title="tr('hint')" type="info" :closable="false" />
  <template v-if="allowed"><fieldset v-if="mode==='export'" :disabled="busy"><legend>{{tr('choose')}}</legend><ElCheckboxGroup v-model="selected" :aria-label="tr('choose')" @change="text='';exported=false"><ElCheckbox v-for="row in rows" :key="row.id" :value="row.id">{{row.revision.content.question}}</ElCheckbox></ElCheckboxGroup></fieldset>
    <ElButton v-if="mode==='export'" :disabled="busy||!selected.length||selected.length>8" data-testid="faq-transfer-export" @click="readExport">{{tr('export')}}</ElButton>
    <ElFormItem v-if="mode==='import'" :label="tr('file')"><input type="file" accept=".json,application/json" :disabled="busy" :aria-label="tr('file')" data-testid="faq-transfer-file" @change="readFile"></ElFormItem>
    <ElFormItem :label="tr('package')" :required="mode==='import'"><ElInput v-model="text" type="textarea" :rows="8" :readonly="mode==='export'" :disabled="busy" :aria-label="tr('package')" data-testid="faq-transfer-json" @input="changed" /></ElFormItem>
    <ElButton v-if="mode==='import'" :disabled="busy||!text.trim()" data-testid="faq-transfer-preview" @click="preview">{{tr('preview')}}</ElButton>
    <ElButton v-if="mode==='import'" :disabled="busy||!pending" data-testid="faq-transfer-commit" @click="commit">{{tr('commit')}}</ElButton>
    <ElButton v-if="mode==='export'" :disabled="busy||!exported" @click="download">{{tr('download')}}</ElButton>
    <p v-if="busy" role="status">{{tr('loading')}}</p><p v-if="exported" role="status">{{tr('exported')}}</p>
    <ol v-if="report" data-testid="faq-transfer-report"><li v-for="item in report.items" :key="item.index">{{tr('item')}} {{item.index+1}} · {{tr(item.status as 'ready'|'source_unavailable'|'classification_conflict'|'conflict'|'created'|'replayed')}}
      <p v-if="pending&&item.status==='ready'">{{pending.package.items[item.index]?.content.question}} · {{tr(pending.package.items[item.index]?.origin==='independent'?'independent':pending.package.items[item.index]?.origin==='faq_derived'?'parentDerived':'derived')}} · {{tr('sources')}} {{pending.package.items[item.index]?.sources.length}}</p>
      <ElButton v-if="item.faqId" :disabled="busy" data-testid="faq-transfer-open" @click="close();emit('openFaq',item.faqId)">{{tr('openDraft')}}</ElButton>
    </li></ol>
  </template>
</ArtFormDialog></section></Teleport></template>
<style scoped>fieldset{display:flex;flex-direction:column;margin-block:12px;min-width:0;}fieldset :deep(.el-checkbox){height:auto;white-space:normal;overflow-wrap:anywhere;} :deep(textarea){overflow-wrap:anywhere;} :deep(.el-form-item){margin-block:12px;}</style>
