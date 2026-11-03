<script setup lang="ts">
import {computed,onUnmounted,ref,watch} from 'vue';
import {ElAlert,ElButton,ElCard,ElForm,ElFormItem,ElInput,ElOption,ElSelect} from 'element-plus';
import type {AiKnowledgeOperationItem,AiKnowledgeOperationsPage,AiListKnowledgeOperationsParameters} from '@fullnet/client-contracts';
import {listKnowledgeOperations} from '../api/ai-knowledge-operations';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeOperationsText} from '../i18n/ai-knowledge-operations';
import AiKnowledgeBaseSelect from '../components/AiKnowledgeBaseSelect.vue';
import AiKnowledgeIndexDialog from '../components/AiKnowledgeIndexDialog.vue';
import AiKnowledgeFaqLifecycleDialog from '../components/AiKnowledgeFaqLifecycleDialog.vue';
import AiKnowledgeQualityPanel from '../components/AiKnowledgeQualityPanel.vue';
const session=useSessionStore();const {locale}=useAdminI18n();const tr=(key:Parameters<typeof knowledgeOperationsText>[1])=>knowledgeOperationsText(locale.value,key);
const required=['ai.knowledge_operations.read','ai.knowledge_bases.read','ai.knowledge_documents.read','ai.knowledge_documents.index_read','ai.knowledge_publications.read','ai.knowledge_faqs.read'];
const canRead=computed(()=>!session.switching&&required.every(code=>session.can(code)));
const canInspect=computed(()=>canRead.value&&session.can('ai.knowledge_documents.source_read'));
const base=ref(''),view=ref('all'),from=ref(''),until=ref('');const result=ref<AiKnowledgeOperationsPage>();const busy=ref(false),failed=ref(false);
const selected=ref<AiKnowledgeOperationItem>();const indexOpen=ref(false),faqOpen=ref(false);let epoch=0,controller:AbortController|undefined;
function clear(){++epoch;controller?.abort();result.value=undefined;selected.value=undefined;indexOpen.value=false;faqOpen.value=false;busy.value=false;}
function state(key:string){return tr((key==='failed'?'failedState':key) as Parameters<typeof tr>[0])??tr('unknown');}
async function load(next=false){
  if(!canRead.value||busy.value)return;const previous=result.value;
  const input:AiListKnowledgeOperationsParameters={knowledgeBaseId:base.value||undefined,view:view.value};
  if(next){if(!previous?.nextAfterId)return;Object.assign(input,{afterId:previous.nextAfterId,fromUtc:previous.fromUtc,toUtc:previous.toUtc});}
  else{if(from.value)Object.assign(input,{fromUtc:new Date(from.value+'T00:00:00.000Z').toISOString()});if(until.value)Object.assign(input,{toUtc:new Date(Math.min(Date.now(),Date.parse(until.value+'T23:59:59.999Z'))).toISOString()});}
  clear();const current=++epoch;controller=new AbortController();busy.value=true;failed.value=false;
  try{const value=await listKnowledgeOperations(input,controller.signal);if(current===epoch&&canRead.value)result.value=value;}
  catch{if(current===epoch){result.value=undefined;failed.value=true;}}
  finally{if(current===epoch)busy.value=false;}
}
function inspect(row:AiKnowledgeOperationItem){if(!canInspect.value)return;selected.value=row;if(row.sourceKind==='faq-revision-v1')faqOpen.value=true;else indexOpen.value=true;}
watch([base,view,from,until],()=>{clear();failed.value=false;});
watch(()=>[session.currentUser,session.currentContextName,session.switching,canRead.value],()=>{clear();base.value='';},{deep:true});
onUnmounted(clear);
</script>
<template><ElCard data-testid="knowledge-operations"><h1>{{tr('title')}}</h1><ElAlert :title="tr('hint')" type="info" :closable="false"/>
  <AiKnowledgeQualityPanel :knowledge-base-id="base"/>
  <p>{{tr('progress')}}</p><p>{{tr('windowHint')}}</p><ElAlert v-if="failed||!canRead" :title="tr('unavailable')" type="error" :closable="false" data-testid="operations-error"/>
  <template v-if="canRead"><ElForm class="operations-filters" label-position="top" @submit.prevent="load()">
    <ElFormItem><AiKnowledgeBaseSelect v-model="base" :disabled="busy"/><ElButton :disabled="busy||!base" @click="base=''">{{tr('allBases')}}</ElButton></ElFormItem>
    <ElFormItem><ElSelect v-model="view" class="operations-view-select" :aria-label="tr('title')" :disabled="busy" data-testid="operations-view"><ElOption v-for="key in ['all','failed','expired']" :key="key" :value="key" :label="tr(key as 'all'|'failed'|'expired')"/></ElSelect></ElFormItem>
    <ElFormItem :label="tr('from')"><ElInput v-model="from" type="date" :aria-label="tr('from')" :disabled="busy" data-testid="operations-from"/></ElFormItem>
    <ElFormItem :label="tr('until')"><ElInput v-model="until" type="date" :aria-label="tr('until')" :disabled="busy" data-testid="operations-until"/></ElFormItem>
    <ElButton native-type="submit" :disabled="busy" data-testid="operations-refresh">{{tr('refresh')}}</ElButton>
  </ElForm><p v-if="busy" role="status">{{tr('loading')}}</p>
  <p v-if="result" class="identity">{{tr('window')}}: {{result.fromUtc}} — {{result.toUtc}}</p><p v-if="result&&!result.items.length">{{tr('empty')}}</p>
  <div class="operations-rows"><article v-for="row in result?.items" :key="row.versionId" class="operations-row" data-testid="operations-row">
    <h3>{{row.sourceKind==='document-v1'?tr('document'):tr('faq')}} · {{row.versionNumber}}</h3>
    <p class="identity">{{row.knowledgeBaseId}} · {{row.documentId}}</p><p class="identity">{{tr('version')}}: {{row.versionId}}</p>
    <dl><dt>{{tr('preparation')}}</dt><dd>{{state(row.preparationState)}}</dd><dt>{{tr('index')}}</dt><dd data-testid="operations-task-state">{{state(row.taskState)}} · {{tr('attempts')}} {{row.attemptCount}}</dd>
      <dt>{{tr('review')}}</dt><dd>{{state(row.reviewState)}}</dd><dt>{{tr('publication')}}</dt><dd data-testid="operations-publication-state">{{state(row.publicationState)}}</dd>
      <dt>{{tr('created')}}</dt><dd>{{row.createdAtUtc}}</dd><dt>{{tr('updated')}}</dt><dd>{{row.taskUpdatedAtUtc??tr('notRecorded')}}</dd>
      <template v-if="row.publicationId"><dt>{{tr('publicationVersion')}}</dt><dd class="identity">{{row.publicationVersionId}}</dd><dt>{{tr('effective')}}</dt><dd>{{row.effectiveFromUtc}} — {{row.effectiveToUtc}}</dd></template></dl>
    <p v-if="row.hasActiveGeneration">{{tr('active')}}</p><p v-if="row.failureCode" role="status">{{row.failureCode}}</p>
    <ElButton v-if="canInspect" data-testid="operations-inspect" @click="inspect(row)">{{tr(row.sourceKind==='document-v1'?'inspectIndex':'inspectFaq')}}</ElButton>
  </article></div><ElButton v-if="result?.nextAfterId" :disabled="busy" data-testid="operations-next" @click="load(true)">{{tr('next')}}</ElButton></template>
  <AiKnowledgeIndexDialog v-if="selected?.sourceKind==='document-v1'" v-model:open="indexOpen" :knowledge-base-id="selected.knowledgeBaseId" :document-id="selected.documentId" :version-id="selected.versionId"/>
  <AiKnowledgeFaqLifecycleDialog v-if="selected?.faqId" v-model:open="faqOpen" :knowledge-base-id="selected.knowledgeBaseId" :faq-id="selected.faqId" @unavailable="clear();failed=true"/>
</ElCard></template>
<style scoped>
.operations-filters{display:flex;flex-wrap:wrap;gap:12px;align-items:end;margin-block:16px}.operations-filters :deep(.el-form-item){max-width:100%;margin-bottom:0}
.operations-view-select{width:220px;max-width:100%}
.operations-rows{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,360px),1fr));gap:16px;margin-block:16px}
.operations-row{border:1px solid var(--el-border-color);border-radius:8px;padding:16px;min-width:0;overflow-wrap:anywhere}.identity{overflow-wrap:anywhere}
dl{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,2fr);gap:8px}dd{margin:0;overflow-wrap:anywhere}
@media(max-width:480px){dl{grid-template-columns:minmax(0,1fr)}.operations-filters{align-items:stretch;flex-direction:column}}
</style>
