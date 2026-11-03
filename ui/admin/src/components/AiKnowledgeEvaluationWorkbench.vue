<script setup lang="ts">
import {computed,onUnmounted,ref,watch} from 'vue';
import {ElAlert,ElButton,ElCheckbox,ElDialog,ElForm,ElFormItem,ElInput,ElOption,ElSelect} from 'element-plus';
import {isFullNetProblemDetails,type AiFeedbackEvaluationSample,type AiKnowledgeEvaluationCaseInput,
  type AiKnowledgeEvaluationWorkspace,type AiKnowledgeEvaluationPage,type AiKnowledgeEvaluationMutation,type SaveAiKnowledgeEvaluationSet,type AiKnowledgeEvaluationPreparedFeedback} from '@fullnet/client-contracts';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeEvaluationText} from '../i18n/ai-knowledge-evaluation';
import * as api from '../api/ai-knowledge-evaluation';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
import AiKnowledgeEvaluationRunsPanel from './AiKnowledgeEvaluationRunsPanel.vue';

const props=defineProps<{candidate?:{token:string;sample:AiFeedbackEvaluationSample}}>();
const emit=defineEmits<{persisted:[revisionId:string,deliveryState:string];unavailable:[]}>();
const session=useSessionStore();const {locale}=useAdminI18n();const tr=(key:Parameters<typeof knowledgeEvaluationText>[1])=>knowledgeEvaluationText(locale.value,key);
const allowed=(action:string)=>!session.switching&&session.can('ai.knowledge_bases.read')&&session.can('ai.knowledge_evaluation_sets.read')&&session.can('ai.knowledge_evaluation_sets.'+action);
const canRead=computed(()=>allowed('read'));const canOpen=computed(()=>canRead.value&&(!props.candidate||allowed('create')&&allowed('feedback')&&session.can('ai.knowledge_answer_feedback.evaluation_admit')));
const open=ref(false),base=ref(''),busy=ref(false);const page=ref<AiKnowledgeEvaluationPage>();const workspace=ref<AiKnowledgeEvaluationWorkspace>();
const history=ref<AiKnowledgeEvaluationWorkspace>();const historicalId=ref('');const savedId=ref('');const editing=ref(false),confirm=ref(false),reviewConfirm=ref(false);
type Mutable<T>={-readonly[K in keyof T]:T[K]};
type FormCase=Omit<Mutable<AiKnowledgeEvaluationCaseInput>,'sample'>&{sample:Mutable<AiFeedbackEvaluationSample>};
const name=ref('');const cases=ref<FormCase[]>([]);const state=ref<'saved'|'recorded'|'unavailable'|'failed'|'invalid'>();
const canWrite=computed(()=>allowed(workspace.value?'append':'create'));
const candidateReady=computed(()=>!!workspace.value||!props.candidate||!!prepared.value&&!!cases.value[0]?.feedbackCandidateToken);
let epoch=0;let controller:AbortController|undefined;
let pending:{permission:string;key:string;send:(signal:AbortSignal)=>Promise<AiKnowledgeEvaluationMutation>}|undefined;
const uncertain=ref(false);
const prepared=ref<AiKnowledgeEvaluationPreparedFeedback>();
function stop(){++epoch;controller?.abort();controller=undefined;busy.value=false;}
function clear(){page.value=undefined;workspace.value=undefined;history.value=undefined;historicalId.value='';savedId.value='';cases.value=[];name.value='';editing.value=false;confirm.value=false;reviewConfirm.value=false;pending=undefined;uncertain.value=false;prepared.value=undefined;}
function reset(){stop();open.value=false;base.value='';clear();state.value=undefined;}
function show(){if(!canOpen.value)return;reset();open.value=true;}
function blank():FormCase{return {id:null,independentBasis:'',feedbackCandidateToken:null,sample:{languageTag:'zh-CN',question:'',expectedAnswer:'',evidence:'',shouldRefuse:false}};}
function edit(current?:AiKnowledgeEvaluationWorkspace){if(!allowed(current?'append':'create')||busy.value||uncertain.value)return;workspace.value=current;history.value=undefined;name.value=current?.revision.name??'';
  cases.value=current?current.revision.cases.map(item=>({id:item.id,sample:{...item.sample},independentBasis:item.independentBasis,feedbackCandidateToken:null}))
    :props.candidate?[{id:null,sample:{...props.candidate.sample},independentBasis:null,feedbackCandidateToken:null}]:[blank()];
  prepared.value=undefined;
  editing.value=true;confirm.value=false;reviewConfirm.value=false;state.value=undefined;
}
function key(){const bytes=crypto.getRandomValues(new Uint8Array(16));let now=BigInt(Date.now());for(let i=5;i>=0;i--){bytes[i]=Number(now&255n);now>>=8n;}bytes[6]=(bytes[6]!&15)|112;bytes[8]=(bytes[8]!&63)|128;const hex=Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join('');return `${hex.slice(0,8)}-${hex.slice(8,12)}-${hex.slice(12,16)}-${hex.slice(16,20)}-${hex.slice(20)}`;}
async function run(action:(signal:AbortSignal,current:()=>boolean)=>Promise<void>,mutation=false){
  if(!open.value||!canRead.value||!base.value||busy.value)return;stop();const generation=++epoch;const abort=new AbortController();controller=abort;busy.value=true;state.value=undefined;
  const current=()=>generation===epoch&&!abort.signal.aborted&&open.value&&canRead.value;
  try{await action(abort.signal,current);}catch(error){if(!current())return;
    if(isFullNetProblemDetails(error)&&[401,403,404,409].includes(error.status)){clear();state.value='unavailable';emit('unavailable');}
    else if(isFullNetProblemDetails(error)&&[400,422].includes(error.status)){pending=undefined;uncertain.value=false;state.value='invalid';}
    else{page.value=undefined;workspace.value=undefined;history.value=undefined;state.value='failed';uncertain.value=mutation&&!!pending;}
  }finally{if(generation===epoch)busy.value=false;}
}
async function load(next=false){if(uncertain.value)return;const after=next?page.value?.nextAfterId:undefined;clear();await run(async(signal,current)=>{const result=await api.listEvaluationSets({knowledgeBaseId:base.value,...(after?{afterId:after}:{})},signal);if(current())page.value=result;});}
async function select(id:string){if(uncertain.value)return;clear();await run(async(signal,current)=>{const result=await api.getEvaluationSet({knowledgeBaseId:base.value,setId:id},signal);if(current())workspace.value=result;});}
async function send(){const original=pending;if(!original||!allowed(original.permission)||uncertain.value&&state.value!=='failed')return;
  await run(async(signal,current)=>{const result=await original.send(signal);if(!current()||pending!==original||!allowed(original.permission))return;
    clear();savedId.value=result.savedRevisionId;workspace.value=result.workspace??undefined;state.value=result.deliveryState==='available'?'saved':'recorded';
    emit('persisted',result.savedRevisionId,result.deliveryState);
  },true);
}
async function prepareFeedback(){if(!props.candidate||!allowed('feedback')||!editing.value||workspace.value||busy.value||uncertain.value)return;
  prepared.value=undefined;confirm.value=false;const first=cases.value[0];if(!first)return;first.feedbackCandidateToken=null;
  const input={knowledgeBaseId:base.value,body:{candidateToken:props.candidate.token}};
  await run(async(signal,current)=>{const value=await api.prepareEvaluationFeedback(input,signal);if(current()&&first===cases.value[0]){prepared.value=value;first.feedbackCandidateToken=value.persistenceToken;}});
}
const isCandidateCase=(index:number)=>!workspace.value&&!!props.candidate&&index===0;
async function save(freeze=false){if(!canWrite.value||!editing.value||!confirm.value||busy.value||uncertain.value||freeze&&!allowed('freeze')
  ||!workspace.value&&props.candidate&&(!prepared.value||!cases.value[0]?.feedbackCandidateToken))return;
  const current=workspace.value;const request:SaveAiKnowledgeEvaluationSet={requestKey:key(),expectedRevisionId:current?.revision.id??null,expectedVersion:current?.version??0,
    name:name.value,cases:cases.value.map(item=>({...item,sample:{...item.sample}})),confirmManualRedaction:true,freeze};
  const knowledgeBaseId=base.value;
  pending={permission:current?'append':'create',key:request.requestKey,send:signal=>current?api.appendEvaluationSet({knowledgeBaseId,setId:current.id,body:request},signal):api.createEvaluationSet({knowledgeBaseId,body:request},signal)};
  await send();
}
async function review(decision:'approved'|'withdrawn'){const current=workspace.value;const permission=decision==='approved'?'approve':'withdraw';
  if(!current||!allowed(permission)||busy.value||uncertain.value||decision==='approved'&&(!reviewConfirm.value||!current.canIndependentlyApprove))return;
  const body={requestKey:key(),expectedRevisionId:current.revision.id,expectedVersion:current.version,decision,confirmIndependentRedactionReview:decision==='approved'};
  const input={knowledgeBaseId:base.value,setId:current.id,body};pending={permission,key:body.requestKey,send:signal=>decision==='approved'?api.approveEvaluationSet(input,signal):api.withdrawEvaluationSet(input,signal)};await send();
}
async function copy(){const current=workspace.value;if(!current||!allowed('copy')||busy.value||uncertain.value)return;const input={knowledgeBaseId:base.value,setId:current.id,body:{requestKey:key(),expectedRevisionId:current.revision.id,expectedVersion:current.version}};
  pending={permission:'copy',key:input.body.requestKey,send:signal=>api.copyEvaluationSet(input,signal)};await send();
}
async function preview(){const current=workspace.value;if(!current||!historicalId.value||uncertain.value)return;history.value=undefined;await run(async(signal,active)=>{const value=await api.getEvaluationSet({knowledgeBaseId:base.value,setId:current.id,revisionId:historicalId.value},signal);if(active())history.value=value;});}
async function restore(){const current=workspace.value;const previous=history.value;if(!current||!previous||!allowed('restore')||busy.value||uncertain.value)return;
  const input={knowledgeBaseId:base.value,setId:current.id,body:{requestKey:key(),expectedRevisionId:current.revision.id,expectedVersion:current.version,revisionId:previous.revision.id}};
  pending={permission:'restore',key:input.body.requestKey,send:signal=>api.restoreEvaluationSet(input,signal)};await send();
}
watch(base,()=>{stop();clear();state.value=undefined;});
watch(()=>[session.currentUser,session.currentContextName,session.switching,props.candidate],reset,{deep:true,flush:'sync'});
watch([name,cases],()=>{confirm.value=false;},{deep:true});
onUnmounted(reset);
</script>
<template>
  <ElButton v-if="canOpen" data-testid="evaluation-sets-open" @click="show">{{tr(candidate?'persist':'open')}}</ElButton>
  <!-- section 保留弹层的语义范围，避免第三方 header 被识别为额外页面 banner。 -->
  <Teleport to="body"><section :aria-label="tr('title')">
  <ElDialog v-model="open" :title="tr('title')" width="min(960px,95vw)" :append-to-body="false" class="evaluation-modal" @closed="reset">
    <div class="evaluation-workbench" data-testid="evaluation-sets"><p>{{tr('hint')}}</p><p>{{tr('sourceHint')}}</p><p>{{tr('ownerHint')}}</p>
      <ElForm label-position="top" @submit.prevent="load()"><ElFormItem :label="tr('base')"><AiKnowledgeBaseSelect v-model="base" :disabled="busy||uncertain"/></ElFormItem>
        <ElButton v-if="canRead" native-type="submit" :disabled="busy||!base||uncertain" data-testid="evaluation-sets-refresh">{{tr('refresh')}}</ElButton>
        <ElButton v-if="allowed('create')" :disabled="busy||!base||uncertain" @click="edit()">{{tr(candidate?'persist':'create')}}</ElButton>
      </ElForm>
      <ElAlert v-if="state" :title="tr(state)" :type="state==='saved'?'success':state==='recorded'?'warning':'error'" :closable="false"/>
      <p v-if="savedId" class="identity">{{tr('savedId')}}: {{savedId}}</p><p v-if="busy" role="status">{{tr('loading')}}</p>
      <ElButton v-if="uncertain&&pending&&allowed(pending.permission)" :disabled="busy" @click="send">{{tr('retry')}}</ElButton>
      <p v-if="page&&!page.items.length">{{tr('empty')}}</p>
      <div v-for="item in page?.items" :key="item.id" class="set-row"><span>{{item.revision.name}} · {{item.revision.state==='frozen'?tr('frozen'):tr('draft')}}</span>
        <ElButton v-if="canRead" :disabled="busy||uncertain" @click="select(item.id)">{{tr('open')}}</ElButton></div>
      <ElButton v-if="page?.nextAfterId&&canRead" :disabled="busy||uncertain" @click="load(true)">{{tr('next')}}</ElButton>
      <section v-if="workspace&&!editing" data-testid="evaluation-workspace"><h3>{{workspace.revision.name}}</h3>
        <p class="identity">{{workspace.id}} · {{tr('revision')}} {{workspace.revision.id}} · {{tr('version')}} {{workspace.version}}</p>
        <p>{{tr('sources')}}: {{workspace.revision.sourceCount}} · {{tr('approval')}}: {{tr(workspace.approvalState as 'none'|'approved'|'withdrawn')}}</p>
        <p class="identity">{{tr('sourceHash')}}: {{workspace.revision.sourceHash}}</p>
        <article v-for="item in workspace.revision.cases" :key="item.id" class="case"><h4>{{item.sourceKey?tr('derived'):tr('independent')}}</h4><p>{{item.sample.question}}</p><p>{{item.sample.expectedAnswer}}</p><p>{{item.sample.evidence}}</p><p v-if="item.independentBasis">{{item.independentBasis}}</p></article>
        <div class="actions"><ElButton v-if="allowed('append')" :disabled="busy||uncertain" @click="edit(workspace)">{{tr('edit')}}</ElButton>
          <ElButton v-if="allowed('copy')" :disabled="busy||uncertain" @click="copy">{{tr('copy')}}</ElButton>
          <ElButton v-if="allowed('withdraw')&&workspace.approvalState==='approved'" :disabled="busy||uncertain" @click="review('withdrawn')">{{tr('withdraw')}}</ElButton></div>
        <template v-if="allowed('approve')&&workspace.canIndependentlyApprove"><ElCheckbox v-model="reviewConfirm" :disabled="busy||uncertain">{{tr('reviewConfirm')}}</ElCheckbox>
          <ElButton :disabled="busy||uncertain||!reviewConfirm" @click="review('approved')">{{tr('approve')}}</ElButton></template>
        <ElForm label-position="top" @submit.prevent="preview"><ElFormItem :label="tr('historyId')"><ElInput v-model="historicalId" :disabled="busy||uncertain"/></ElFormItem>
          <ElButton v-if="canRead" native-type="submit" :disabled="busy||uncertain||!historicalId">{{tr('preview')}}</ElButton></ElForm>
        <section v-if="history"><h4>{{tr('previewTitle')}}</h4><p class="identity">{{history.revision.id}}</p><p v-for="item in history.revision.cases" :key="item.id">{{item.sample.question}} · {{item.sample.expectedAnswer}}</p>
          <ElButton v-if="allowed('restore')" :disabled="busy||uncertain" @click="restore">{{tr('restore')}}</ElButton></section>
      </section>
      <ElForm v-if="editing&&canWrite" label-position="top" @submit.prevent="save()"><ElFormItem :label="tr('name')" required><ElInput v-model="name" maxlength="160" :disabled="busy||uncertain"/></ElFormItem>
        <template v-if="candidate&&!workspace"><ElButton v-if="allowed('feedback')" :disabled="busy||uncertain" @click="prepareFeedback">{{tr('prepareFeedback')}}</ElButton>
          <p v-if="prepared" class="identity">{{prepared.sourceHash}} · {{prepared.sampleHash}} · {{prepared.expiresAtUtc}}</p></template>
        <fieldset v-for="(item,index) in cases" :key="index" class="case"><legend>{{tr('sample')}} {{index+1}}</legend>
          <p v-if="isCandidateCase(index)||item.feedbackCandidateToken||workspace?.revision.cases.find(c=>c.id===item.id)?.sourceKey">{{tr('derived')}}</p>
          <template v-if="isCandidateCase(index)"><p>{{item.sample.languageTag}}</p><p>{{item.sample.question}}</p><p>{{item.sample.expectedAnswer}}</p><p>{{item.sample.evidence}}</p><p>{{tr('refuse')}}: {{item.sample.shouldRefuse}}</p></template>
          <template v-else>
          <ElFormItem :label="tr('language')"><ElSelect v-model="item.sample.languageTag" :disabled="busy||uncertain||!!item.feedbackCandidateToken"><ElOption value="zh-CN" label="zh-CN"/><ElOption value="en-US" label="en-US"/></ElSelect></ElFormItem>
          <ElFormItem :label="tr('question')" required><ElInput v-model="item.sample.question" type="textarea" maxlength="2048" :disabled="busy||uncertain||!!item.feedbackCandidateToken"/></ElFormItem>
          <ElFormItem :label="tr('answer')" required><ElInput v-model="item.sample.expectedAnswer" type="textarea" maxlength="4096" :disabled="busy||uncertain||!!item.feedbackCandidateToken"/></ElFormItem>
          <ElFormItem :label="tr('evidence')" required><ElInput v-model="item.sample.evidence" type="textarea" maxlength="8192" :disabled="busy||uncertain||!!item.feedbackCandidateToken"/></ElFormItem>
          <ElCheckbox v-model="item.sample.shouldRefuse" :disabled="busy||uncertain||!!item.feedbackCandidateToken">{{tr('refuse')}}</ElCheckbox>
          </template>
          <ElFormItem v-if="!item.id&&!item.feedbackCandidateToken&&!isCandidateCase(index)" :label="tr('basis')" required><ElInput v-model="item.independentBasis" type="textarea" maxlength="1024" :disabled="busy||uncertain"/></ElFormItem>
          <p v-else-if="item.independentBasis">{{tr('basis')}}: {{item.independentBasis}}</p>
          <ElButton :disabled="busy||uncertain||cases.length===1||isCandidateCase(index)" @click="cases.splice(index,1)">{{tr('remove')}}</ElButton>
        </fieldset>
        <ElButton :disabled="busy||uncertain||cases.length>=8" @click="cases.push(blank())">{{tr('add')}}</ElButton>
        <ElCheckbox v-model="confirm" :disabled="busy||uncertain" data-testid="evaluation-manual-confirm">{{tr('confirm')}}</ElCheckbox>
        <div class="actions"><ElButton native-type="submit" :disabled="busy||uncertain||!confirm||!candidateReady">{{tr('save')}}</ElButton>
          <ElButton v-if="allowed('freeze')" :disabled="busy||uncertain||!confirm||!candidateReady" @click="save(true)">{{tr('freeze')}}</ElButton>
          <ElButton :disabled="busy||uncertain" @click="cases=[];editing=false">{{tr('cancelEdit')}}</ElButton></div>
      </ElForm>
      <AiKnowledgeEvaluationRunsPanel v-if="workspace&&!editing" :knowledge-base-id="base" :workspace="workspace" @unavailable="clear();state='unavailable';emit('unavailable')"/>
    </div><template #footer><ElButton @click="reset">{{tr('close')}}</ElButton></template>
  </ElDialog>
  </section></Teleport>
</template>
<style scoped>
.evaluation-workbench{min-width:0;overflow-wrap:anywhere}.identity{overflow-wrap:anywhere}.case{margin-block:16px;padding:12px;border:1px solid var(--el-border-color);border-radius:6px;min-width:0}.case p{white-space:pre-wrap}.actions,.set-row{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin-block:12px}.evaluation-workbench :deep(.el-checkbox){white-space:normal;height:auto;margin-block:12px}.evaluation-workbench :deep(.el-checkbox__label){white-space:normal;overflow-wrap:anywhere}.evaluation-workbench :deep(.el-button){height:auto;min-height:32px;white-space:normal;max-width:100%}.evaluation-workbench :deep(.el-select){width:100%}
/* 传送弹层的选择值明确使用输入底色和正文色，保持与遮罩叠放时的文字对比度。 */
.evaluation-workbench :deep(.el-select__selected-item.el-select__placeholder > span){background-color:var(--el-fill-color-blank);color:var(--el-text-color-primary)}
</style>
