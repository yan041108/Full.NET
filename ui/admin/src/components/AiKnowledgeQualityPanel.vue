<script setup lang="ts">
import {computed,defineAsyncComponent,onUnmounted,ref,watch} from 'vue';
import {ElAlert,ElButton,ElForm,ElFormItem,ElInput} from 'element-plus';
import type {AiKnowledgeFinanceReport,AiKnowledgeQualityObservation} from '@fullnet/client-contracts';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeQualityText} from '../i18n/ai-knowledge-quality';
import {getKnowledgeFinance,getKnowledgeQuality} from '../api/ai-knowledge-evaluation-runs';
const props=defineProps<{knowledgeBaseId?:string}>();const session=useSessionStore();const {locale}=useAdminI18n();const tr=(key:Parameters<typeof knowledgeQualityText>[1])=>knowledgeQualityText(locale.value,key);
const canFinance=computed(()=>!session.switching&&session.can('ai.knowledge_finance.read'));
const canQuality=computed(()=>!session.switching&&!!props.knowledgeBaseId&&['ai.knowledge_operations.read','ai.knowledge_bases.read','ai.knowledge_answer_feedback.review_read'].every(key=>session.can(key)));
const FeedbackReview=defineAsyncComponent(()=>import('./AiFeedbackReviewPanel.vue'));
const from=ref(''),to=ref(''),busy=ref(false),failed=ref(false);const finance=ref<AiKnowledgeFinanceReport>();const quality=ref<AiKnowledgeQualityObservation>();let epoch=0,abort:AbortController|undefined;
function clear(){++epoch;abort?.abort();abort=undefined;finance.value=undefined;quality.value=undefined;busy.value=false;}
async function load(kind:'finance'|'quality'){
 if(busy.value||(kind==='finance'?!canFinance.value:!canQuality.value))return;
 clear();const current=++epoch;abort=new AbortController();busy.value=true;failed.value=false;
 const range={...(from.value?{fromUtc:new Date(from.value+'T00:00:00Z').toISOString()}:{}),...(to.value?{toUtc:new Date(Math.min(Date.now(),Date.parse(to.value+'T23:59:59.999Z'))).toISOString()}:{})};
 try{if(kind==='finance'){const value=await getKnowledgeFinance(range,abort.signal);if(current===epoch&&canFinance.value)finance.value=value;}
  else{const value=await getKnowledgeQuality({knowledgeBaseId:props.knowledgeBaseId!,...range},abort.signal);if(current===epoch&&canQuality.value)quality.value=value;}}
 catch{if(current===epoch){finance.value=undefined;quality.value=undefined;failed.value=true;}}finally{if(current===epoch)busy.value=false;}
}
watch(()=>[props.knowledgeBaseId,session.currentUser,session.currentContextName,session.switching,canFinance.value,canQuality.value],()=>{clear();failed.value=false;},{deep:true,flush:'sync'});
watch([from,to],clear);onUnmounted(clear);
</script>
<template><section v-if="canFinance||canQuality" class="knowledge-quality" data-testid="knowledge-quality" :aria-label="tr('title')"><h2>{{tr('title')}}</h2><p>{{tr('hint')}}</p><p>{{tr('window')}}</p>
 <ElForm class="quality-controls" label-position="top" @submit.prevent><ElFormItem :label="tr('from')"><ElInput v-model="from" type="date" :aria-label="tr('from')" :disabled="busy"/></ElFormItem><ElFormItem :label="tr('to')"><ElInput v-model="to" type="date" :aria-label="tr('to')" :disabled="busy"/></ElFormItem></ElForm>
 <div class="quality-controls"><ElButton v-if="canFinance" :disabled="busy" @click="load('finance')">{{tr('finance')}}</ElButton><ElButton v-if="canQuality" :disabled="busy" @click="load('quality')">{{tr('quality')}}</ElButton></div>
 <!-- 复用原审核/脱敏入口及其聊天读取父权限；统计读取本身不授予私人正文或审核写入。 -->
 <FeedbackReview v-if="canQuality&&session.can('ai.chat.sessions.read')"/>
 <ElAlert v-if="failed" :title="tr('unavailable')" type="error" :closable="false"/>
 <section v-if="finance" :aria-label="tr('scope')" data-testid="knowledge-finance"><h3>{{tr('scope')}}</h3><p>{{finance.fromUtc}} — {{finance.toUtc}}</p><p>{{tr('financeHint')}}</p><p v-if="finance.items.length===0">{{tr('noRows')}}</p>
  <article v-for="(item,index) in finance.items" :key="index" class="quality-item"><p>{{tr(item.attribution as 'unattributed'|'explicit_operation')}} · {{item.knowledgeBaseId??tr('unattributed')}}</p><p>{{tr('month')}}: {{item.month}} · {{tr('price')}}: {{item.priceVersionId??tr('unknown')}} · {{tr('currency')}}: {{item.currency??tr('unknown')}}</p>
   <p>{{tr('requests')}} {{item.requests}} · {{tr('known')}} {{item.known}} · {{tr('unknownUsage')}} {{item.unknown}} · {{tr('reserved')}} {{item.reserved}} · {{tr('missing')}} {{item.missingCost}}</p><p>{{tr('recorded')}}: {{item.recordedCost??tr('unknown')}} · {{tr('conservative')}}: {{item.conservativeReservation??tr('unknown')}}</p></article>
 </section>
 <section v-if="quality" :aria-label="tr('quality')" data-testid="knowledge-outcomes"><p>{{quality.fromUtc}} — {{quality.toUtc}}</p><p>{{tr('answers')}} {{quality.recordedAnswers}} · {{tr('refused')}} {{quality.refused}} · {{tr('negative')}} {{quality.currentNegativeFeedback}}</p><p>{{tr('measured')}} {{quality.measured}} · {{tr('mean')}} {{quality.meanPipelineMs??tr('unknown')}} ms</p><p>{{tr('coverage')}}</p></section>
</section></template>
<style scoped>.knowledge-quality{min-width:0;overflow-wrap:anywhere;margin-block:24px}.quality-controls{display:flex;flex-wrap:wrap;gap:12px;margin-block:12px}.quality-item{padding:12px;border:1px solid var(--el-border-color);border-radius:6px;margin-block:12px;min-width:0}.knowledge-quality :deep(.el-button){max-width:100%;white-space:normal;height:auto;min-height:32px;margin-left:0}</style>
