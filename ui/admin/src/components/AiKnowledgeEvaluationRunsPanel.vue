<script setup lang="ts">
import {computed,onUnmounted,ref,watch} from 'vue';
import {ElAlert,ElButton,ElCheckbox,ElForm,ElFormItem,ElInput,ElOption,ElSelect} from 'element-plus';
import {isFullNetProblemDetails,type AiKnowledgeEvaluationWorkspace,type AiKnowledgeEvaluationStrategy,type AiKnowledgeEvaluationRun,type AiKnowledgeEvaluationCaseResult,type AiKnowledgeEvaluationRunAdmission,type AiKnowledgeEvaluationEntryPage} from '@fullnet/client-contracts';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeRunText} from '../i18n/ai-knowledge-evaluation-runs';
import * as api from '../api/ai-knowledge-evaluation-runs';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
const props=defineProps<{knowledgeBaseId:string;workspace:AiKnowledgeEvaluationWorkspace}>();
const emit=defineEmits<{unavailable:[]}>();const session=useSessionStore();const {locale}=useAdminI18n();const tr=(key:Parameters<typeof knowledgeRunText>[1])=>knowledgeRunText(locale.value,key);
const allowed=(action:string)=>!session.switching&&session.can('ai.knowledge_bases.read')&&session.can('ai.knowledge_evaluation_sets.read')&&session.can('ai.knowledge_evaluation_runs.read')&&session.can('ai.knowledge_evaluation_runs.'+action);
const canRead=computed(()=>allowed('read'));const frozen=computed(()=>props.workspace.approvalState==='approved'&&props.workspace.revision.state==='frozen');
const name=ref(''),baseline=ref('keyword'),candidate=ref('vector');const prepared=ref<Awaited<ReturnType<typeof api.prepareEvaluationStrategy>>>();
const sourceBase=ref(props.knowledgeBaseId);
const strategy=ref<AiKnowledgeEvaluationStrategy>();const run=ref<AiKnowledgeEvaluationRun>();const selected=ref<AiKnowledgeEvaluationCaseResult>();const admission=ref<AiKnowledgeEvaluationRunAdmission>();
const strategies=ref<AiKnowledgeEvaluationEntryPage>(),runs=ref<AiKnowledgeEvaluationEntryPage>();const busy=ref(false),paid=ref(false),human=ref(false),baselineSupports=ref(false),candidateSupports=ref(false),failed=ref(false),uncertain=ref(false),originalId=ref('');
let epoch=0,abort:AbortController|undefined,pending:{permission:string;send:(signal:AbortSignal,current:()=>boolean)=>Promise<void>}|undefined;
function stop(){++epoch;abort?.abort();abort=undefined;busy.value=false;}
function clear(){prepared.value=undefined;strategy.value=undefined;run.value=undefined;selected.value=undefined;admission.value=undefined;strategies.value=undefined;runs.value=undefined;paid.value=false;human.value=false;pending=undefined;uncertain.value=false;}
function reset(){stop();clear();originalId.value='';failed.value=false;}
async function task(action:(signal:AbortSignal,current:()=>boolean)=>Promise<void>,mutation=false){
 if(!canRead.value||busy.value)return;stop();const generation=++epoch;abort=new AbortController();const signal=abort.signal;busy.value=true;failed.value=false;
 const current=()=>generation===epoch&&!signal.aborted&&canRead.value;
 try{await action(signal,current);}catch(error){if(!current())return;
  if(isFullNetProblemDetails(error)&&[400,401,403,404,409,422].includes(error.status)){clear();failed.value=true;emit('unavailable');}
  else{selected.value=undefined;run.value=undefined;uncertain.value=mutation&&!!pending;failed.value=true;}}
 finally{if(generation===epoch)busy.value=false;}
}
async function send(){const action=pending;if(!action||!allowed(action.permission))return;await task(async(signal,current)=>{await action.send(signal,current);if(current()&&pending===action){pending=undefined;uncertain.value=false;}},true);}
async function prepare(){if(!allowed('plan')||!frozen.value||uncertain.value)return;clear();await task(async(signal,current)=>{const value=await api.prepareEvaluationStrategy(props.knowledgeBaseId,props.workspace.revision.cases[0]!.sample.question,signal,sourceBase.value);if(current()){prepared.value=value;name.value=props.workspace.revision.name.slice(0,120);}});}
async function capture(){const source=prepared.value;if(!source||!allowed('plan')||uncertain.value||!frozen.value)return;const strategyId=api.evaluationIdentity();
 const input={knowledgeBaseId:props.knowledgeBaseId,strategyId,body:{requestKey:api.evaluationIdentity(),expectedVersion:0,setId:props.workspace.id,setRevisionId:props.workspace.revision.id,setVersion:props.workspace.version,setApprovalId:props.workspace.approvalId!,
  name:name.value,baseline:baseline.value,candidate:candidate.value,embeddingModelConfigId:source.base.embeddingModelConfigId!,embeddingModelVersion:source.base.embeddingModelVersion!,
  processingModelConfigId:candidate.value==='rewrite_rerank'?source.base.generationModelConfigId:null,processingModelVersion:candidate.value==='rewrite_rerank'?source.base.generationModelVersion:null,documentIds:source.sources.map(x=>x.documentId),expectedSources:source.sources}};
 pending={permission:'plan',send:async(signal,current)=>{const value=await api.captureEvaluationStrategy(input,signal);if(current()){strategy.value=value;admission.value=undefined;run.value=undefined;selected.value=undefined;}}};await send();
}
async function preview(){const currentStrategy=strategy.value;if(!currentStrategy||!allowed('create')||uncertain.value)return;paid.value=false;admission.value=undefined;
 await task(async(signal,current)=>{const value=await api.previewEvaluationRun({knowledgeBaseId:props.knowledgeBaseId,strategyId:currentStrategy.id,revisionId:currentStrategy.revisionId},currentStrategy,signal);if(current())admission.value=value;});}
async function create(){const currentStrategy=strategy.value,currentAdmission=admission.value;if(!currentStrategy||!currentAdmission||!paid.value||!allowed('create')||uncertain.value)return;
 const input={knowledgeBaseId:props.knowledgeBaseId,strategyId:currentStrategy.id,body:{requestKey:api.evaluationIdentity(),strategyRevisionId:currentStrategy.revisionId,admissionDigest:currentAdmission.digest,confirmPaidModelCalls:true}};
 pending={permission:'create',send:async(signal,current)=>{const value=await api.createEvaluationRun(input,signal);if(current()){run.value=value;originalId.value=value.id;selected.value=undefined;paid.value=false;}}};await send();
}
async function catalog(kind:'strategy'|'run',next=false){if(uncertain.value)return;await task(async(signal,current)=>{const page=kind==='strategy'?strategies.value:runs.value;
 const input={knowledgeBaseId:props.knowledgeBaseId,...(next&&page?.nextAfterId?{afterId:page.nextAfterId}:{})};
 const value=await (kind==='strategy'?api.listEvaluationStrategies(input,signal):api.listEvaluationRuns(input,signal));if(current()){if(kind==='strategy')strategies.value=value;else runs.value=value;}});}
async function openStrategy(id:string,revisionId:string){if(uncertain.value)return;clear();await task(async(signal,current)=>{const value=await api.getEvaluationStrategy({knowledgeBaseId:props.knowledgeBaseId,strategyId:id,revisionId},signal);if(current())strategy.value=value;});}
async function openRun(id:string){await task(async(signal,current)=>{const value=await api.getEvaluationRun({knowledgeBaseId:props.knowledgeBaseId,runId:id},signal);
 const bound=await api.getEvaluationStrategy({knowledgeBaseId:props.knowledgeBaseId,strategyId:value.strategyId,revisionId:value.strategyRevisionId},signal);
 if(current()){run.value=value;strategy.value=bound;originalId.value=value.id;selected.value=undefined;}});}
async function inspect(caseId:string){const currentRun=run.value,currentStrategy=strategy.value;if(!currentRun||!currentStrategy)return;selected.value=undefined;human.value=false;baselineSupports.value=false;candidateSupports.value=false;
 await task(async(signal,current)=>{const value=await api.getEvaluationCase({knowledgeBaseId:props.knowledgeBaseId,runId:currentRun.id,caseId},currentStrategy,signal);if(current())selected.value=value;});}
async function act(caseId:string,action:'execute'|'skip'|'close_unknown'){const currentRun=run.value,currentStrategy=strategy.value,item=currentRun?.cases.find(x=>x.id===caseId);
 if(!currentRun||!currentStrategy||!item||!allowed(action)||uncertain.value)return;
 const input={knowledgeBaseId:props.knowledgeBaseId,runId:currentRun.id,caseId,body:{requestKey:api.evaluationIdentity(),expectedVersion:item.version}};
 const call=action==='execute'?api.executeEvaluationCase:action==='skip'?api.skipEvaluationCase:api.closeUnknownEvaluationCase;
 pending={permission:action,send:async(signal,current)=>{await call(input,currentStrategy,signal);const summary=await api.getEvaluationRun({knowledgeBaseId:props.knowledgeBaseId,runId:currentRun.id},signal);
  const detail=await api.getEvaluationCase({knowledgeBaseId:props.knowledgeBaseId,runId:currentRun.id,caseId},currentStrategy,signal);if(current()){run.value=summary;selected.value=detail;human.value=false;baselineSupports.value=false;candidateSupports.value=false;}}};await send();
}
async function review(){const currentRun=run.value,currentStrategy=strategy.value,item=selected.value;if(!currentRun||!currentStrategy||!item||!human.value||!allowed('review')||uncertain.value)return;
 const input={knowledgeBaseId:props.knowledgeBaseId,runId:currentRun.id,caseId:item.case.id,body:{requestKey:api.evaluationIdentity(),expectedVersion:item.case.version,baselineSupportsReference:baselineSupports.value,candidateSupportsReference:candidateSupports.value,confirmHumanReview:true}};
 pending={permission:'review',send:async(signal,current)=>{const value=await api.reviewEvaluationCase(input,currentStrategy,signal);if(current()){selected.value=value;human.value=false;}}};await send();
}
const state=(value:string)=>tr(value as Parameters<typeof tr>[0])??value;
const time=(value:unknown)=>typeof value==='number'?`${value.toFixed(3)} ms`:tr('notRecorded');
watch(()=>[props.knowledgeBaseId,props.workspace.id,props.workspace.version,session.currentUser,session.currentContextName,session.switching,canRead.value],reset,{deep:true,flush:'sync'});
watch(baseline,()=>{if(baseline.value==='vector')candidate.value='rewrite_rerank';});
watch(sourceBase,()=>{stop();clear();});
onUnmounted(reset);
</script>
<template><section v-if="canRead" class="evaluation-runs" data-testid="evaluation-runs" :aria-label="tr('title')"><h3>{{tr('title')}}</h3><p>{{tr('hint')}}</p>
 <AiKnowledgeBaseSelect v-if="allowed('plan')&&frozen" v-model="sourceBase" :disabled="busy||uncertain"/>
 <ElAlert v-if="failed" :title="tr(uncertain?'network':'error')" type="error" :closable="false" role="alert"/>
 <p v-if="originalId" class="identity">{{tr('original')}}: {{originalId}}</p>
 <ElButton v-if="uncertain" :disabled="busy" @click="send">{{tr('replay')}}</ElButton>
 <ElButton v-if="originalId" :disabled="busy" @click="openRun(originalId)">{{tr('refresh')}}</ElButton>
 <div class="actions"><ElButton :disabled="busy||uncertain" @click="catalog('strategy')">{{tr('strategies')}}</ElButton><ElButton :disabled="busy||uncertain" @click="catalog('run')">{{tr('runs')}}</ElButton>
  <ElButton v-if="allowed('plan')&&frozen" :disabled="busy||uncertain" @click="prepare">{{tr('prepare')}}</ElButton></div>
 <p v-if="strategies||runs">{{tr('catalogHint')}}</p><div v-for="item in strategies?.items" :key="item.id" class="actions identity"><ElButton :disabled="busy||uncertain" @click="openStrategy(item.id,item.revisionId)">{{item.id}}</ElButton></div>
 <ElButton v-if="strategies?.nextAfterId" :disabled="busy||uncertain" @click="catalog('strategy',true)">{{tr('more')}}</ElButton>
 <div v-for="item in runs?.items" :key="item.id" class="actions identity"><ElButton :disabled="busy||uncertain" @click="openRun(item.id)">{{item.id}}</ElButton></div><ElButton v-if="runs?.nextAfterId" :disabled="busy||uncertain" @click="catalog('run',true)">{{tr('more')}}</ElButton>
 <ElForm v-if="prepared" label-position="top" @submit.prevent="capture"><ElFormItem :label="tr('name')"><ElInput v-model="name" maxlength="160" :aria-label="tr('name')" :disabled="busy||uncertain"/></ElFormItem>
  <ElFormItem :label="tr('baseline')"><ElSelect v-model="baseline" :aria-label="tr('baseline')" :disabled="busy||uncertain"><ElOption value="keyword" :label="tr('keyword')"/><ElOption value="vector" :label="tr('vector')"/></ElSelect></ElFormItem>
  <ElFormItem :label="tr('candidate')"><ElSelect v-model="candidate" :aria-label="tr('candidate')" :disabled="busy||uncertain"><ElOption v-if="baseline==='keyword'" value="vector" :label="tr('vector')"/><ElOption value="rewrite_rerank" :label="tr('rewrite_rerank')"/></ElSelect></ElFormItem>
  <p>{{tr('sources')}}: {{prepared.sources.length}}</p><p v-for="source in prepared.sources" :key="source.documentId" class="identity">{{source.sourceKind}} · {{source.documentId}} · {{source.publicationId}}</p>
  <ElButton native-type="submit" :disabled="busy||uncertain||!name.trim()">{{tr('capture')}}</ElButton>
 </ElForm>
 <article v-if="strategy" data-testid="evaluation-strategy"><h4>{{strategy.name}}</h4><p>{{state(strategy.baseline)}} → {{state(strategy.candidate)}}</p><p class="identity">{{strategy.revisionId}} · {{strategy.definitionHash}}</p>
  <p class="identity">{{strategy.embeddingModel.id}} / {{strategy.embeddingModel.version}}<template v-if="strategy.processingModel"> · {{strategy.processingModel.id}} / {{strategy.processingModel.version}}</template></p>
  <ElButton v-if="allowed('create')" :disabled="busy||uncertain" @click="preview">{{tr('preview')}}</ElButton>
  <div v-if="admission"><p>{{tr('calls')}}: {{admission.caseCount}} / {{admission.maximumModelCalls}}</p><p>{{admission.budgetPolicy}}</p>
   <ElCheckbox v-model="paid" :disabled="busy||uncertain" data-testid="evaluation-paid-confirm">{{tr('confirm')}}</ElCheckbox><ElButton :disabled="busy||uncertain||!paid" @click="create">{{tr('create')}}</ElButton></div>
 </article>
 <article v-if="run" data-testid="evaluation-run"><h4>{{state(run.state)}}</h4><p>{{tr('total')}}: {{run.total}} · {{tr('succeeded')}} {{run.succeeded}} · {{tr('failed')}} {{run.failed}} · {{tr('unknown')}} {{run.unknown}} · {{tr('skipped')}} {{run.skipped}} · {{tr('not_run')}} {{run.notRun}} · {{tr('running')}} {{run.running}}</p>
  <p>{{tr('measured')}} {{run.measured}} · {{tr('mean')}} {{time(run.meanDurationMs)}} · {{tr('p95')}} {{time(run.p95DurationMs)}}</p>
  <article v-for="item in run.cases" :key="item.id" class="run-case"><p>{{item.sequence+1}} · {{state(item.state)}} · {{time(item.durationMs)}}</p><p class="identity">{{item.id}}</p><div class="actions">
   <ElButton :disabled="busy" @click="inspect(item.id)">{{tr('loadCase')}}</ElButton><ElButton v-if="item.state==='not_run'&&allowed('execute')" :disabled="busy||uncertain" @click="act(item.id,'execute')">{{tr('execute')}}</ElButton>
   <ElButton v-if="item.state==='not_run'&&allowed('skip')" :disabled="busy||uncertain" @click="act(item.id,'skip')">{{tr('skip')}}</ElButton><ElButton v-if="item.state==='running'&&allowed('close_unknown')" :disabled="busy||uncertain" @click="act(item.id,'close_unknown')">{{tr('closeUnknown')}}</ElButton></div></article>
 </article>
 <article v-if="selected&&!busy" data-testid="evaluation-case"><h4>{{tr('question')}}</h4><p class="content">{{selected.sample.question}}</p><h4>{{tr('answer')}}</h4><p class="content">{{selected.sample.expectedAnswer}}</p><h4>{{tr('evidence')}}</h4><p class="content">{{selected.sample.evidence}}</p>
  <p v-if="selected.effectiveQuery">{{tr('effective')}}: {{selected.effectiveQuery}}</p><p v-if="selected.refinementRefused">{{tr('refused')}}</p>
  <section v-for="kind in ['baseline','candidate'] as const" :key="kind" :aria-label="tr(kind)"><h4>{{tr(kind)}}</h4><template v-if="selected[kind]"><p>{{selected[kind]!.strategy}} · {{selected[kind]!.scoreKind}}</p><article v-for="hit in selected[kind]!.hits" :key="hit.source.documentId+':'+hit.sequence" class="run-hit"><p>{{hit.documentTitle}} · {{hit.score}}</p><p class="content">{{hit.text}}</p></article></template></section>
  <section :aria-label="tr('finance')"><h4>{{tr('finance')}}</h4><p>{{tr('financeHint')}}</p><p v-if="selected.financeState==='not_authorized'">{{tr('financeDenied')}}</p><article v-for="operation in selected.operations" :key="operation.id" class="run-hit"><p>{{operation.stage}} · {{operation.usageStatus==='not_recorded'?tr('noLedger'):operation.usageStatus}}</p><p class="identity">{{operation.id}} · {{operation.month}} · {{operation.priceVersionId??tr('notRecorded')}} · {{operation.currency??tr('notRecorded')}}</p><p>{{tr('recorded')}}: {{operation.recordedCost??tr('unknownFee')}} · {{tr('reserved')}}: {{operation.reservedCost??tr('notRecorded')}}</p></article></section>
  <ElForm v-if="selected.case.state==='succeeded'&&allowed('review')" label-position="top" @submit.prevent="review"><h4>{{tr('human')}}</h4><ElCheckbox v-model="baselineSupports" :disabled="busy||uncertain">{{tr('baselineSupports')}}</ElCheckbox><ElCheckbox v-model="candidateSupports" :disabled="busy||uncertain||!selected.candidate">{{tr('candidateSupports')}}</ElCheckbox><ElCheckbox v-model="human" :disabled="busy||uncertain">{{tr('reviewConfirm')}}</ElCheckbox><ElButton native-type="submit" :disabled="busy||uncertain||!human">{{tr('review')}}</ElButton></ElForm>
 </article>
</section></template>
<style scoped>
.evaluation-runs{min-width:0;margin-block:20px;overflow-wrap:anywhere}.identity{overflow-wrap:anywhere}.content{white-space:pre-wrap}.actions{display:flex;flex-wrap:wrap;gap:8px;margin-block:12px}.run-case,.run-hit{padding:12px;border:1px solid var(--el-border-color);border-radius:6px;margin-block:12px;min-width:0}.evaluation-runs :deep(.el-checkbox){white-space:normal;height:auto;margin-block:10px}.evaluation-runs :deep(.el-checkbox__label){white-space:normal;overflow-wrap:anywhere}.evaluation-runs :deep(.el-button){height:auto;min-height:32px;white-space:normal;max-width:100%;margin-left:0}.evaluation-runs :deep(.el-select){width:100%}.evaluation-runs :deep(.el-select__selected-item.el-select__placeholder > span){background-color:var(--el-fill-color-blank);color:var(--el-text-color-primary)}
</style>
