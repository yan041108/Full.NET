<script setup lang="ts">
import { computed, onUnmounted, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeAnswerFeedbackReviewItem, type AiKnowledgeFaqFeedbackPreview,
  type AiKnowledgeFaqDraftWorkspace, type AiCreateFeedbackKnowledgeFaqDraftParameters } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeFaqFeedbackText } from '../i18n/ai-knowledge-faq-feedback';
import { getKnowledgeBase } from '../api/ai-knowledge';
import { previewFeedbackFaq, createFeedbackFaq, type FeedbackPreviewIdentity } from '../api/ai-knowledge-faq-feedback';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
import AiKnowledgeFaqDraftDialog from './AiKnowledgeFaqDraftDialog.vue';

const props=defineProps<{feedback:AiKnowledgeAnswerFeedbackReviewItem;disabled?:boolean}>();
const emit=defineEmits<{unavailable:[]}>();
const session=useSessionStore();const {locale}=useAdminI18n();const text=(key:Parameters<typeof knowledgeFaqFeedbackText>[1])=>knowledgeFaqFeedbackText(locale.value,key);
const permitted=computed(()=>!session.switching&&props.feedback.reviewStateKey==='approved'&&['ai.chat.sessions.read','ai.knowledge_answer_feedback.review_read','ai.knowledge_bases.read',
  'ai.knowledge_faqs.read','ai.knowledge_faqs.write','ai.knowledge_faqs.feedback','ai.knowledge_documents.read','ai.knowledge_documents.source_read','ai.knowledge_documents.index_read'].every(code=>session.can(code)));
const open=ref(false);const base=ref('');const busy=ref(false);const preview=ref<AiKnowledgeFaqFeedbackPreview>();const saved=ref<AiKnowledgeFaqDraftWorkspace>();const editOpen=ref(false);
const unknown=ref(false);const state=ref<'saved'|'restricted'|'unavailable'|'invalid'|'failed'>();
const form=reactive({question:'',aliases:'',category:'',from:'',until:''});let epoch=0;let controller:AbortController|undefined;
let pending:{request:AiCreateFeedbackKnowledgeFaqDraftParameters;expected:FeedbackPreviewIdentity}|undefined;
function stop(){++epoch;controller?.abort();controller=undefined;busy.value=false;}
function clear(){preview.value=undefined;saved.value=undefined;editOpen.value=false;unknown.value=false;pending=undefined;Object.assign(form,{question:'',aliases:'',category:'',from:'',until:''});}
function close(){stop();open.value=false;clear();base.value='';state.value=undefined;}
function show(){if(!permitted.value||props.disabled)return;close();open.value=true;}
function unavailable(){clear();state.value='unavailable';emit('unavailable');}
function key(){const b=crypto.getRandomValues(new Uint8Array(16));let now=BigInt(Date.now());for(let i=5;i>=0;--i){b[i]=Number(now&255n);now>>=8n;}b[6]=(b[6]!&15)|112;b[8]=(b[8]!&63)|128;const h=Array.from(b,v=>v.toString(16).padStart(2,'0')).join('');return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`;}
async function run(action:(signal:AbortSignal,current:()=>boolean)=>Promise<void>,saving=false){
  if(!open.value||!permitted.value||busy.value)return;stop();const currentEpoch=++epoch;controller=new AbortController();const signal=controller.signal;busy.value=true;state.value=undefined;
  const current=()=>currentEpoch===epoch&&!signal.aborted&&open.value&&permitted.value;
  try{await action(signal,current);}catch(error){if(!current())return;
    if(isFullNetProblemDetails(error)&&[401,403,404,409].includes(error.status)){unavailable();}
    else if(isFullNetProblemDetails(error)&&[400,422].includes(error.status)){pending=undefined;unknown.value=false;state.value='invalid';}
    else{preview.value=undefined;saved.value=undefined;editOpen.value=false;state.value='failed';unknown.value=saving&&!!pending;}
  }finally{if(currentEpoch===epoch)busy.value=false;}
}
async function read(){
  if(!base.value)return;const prior=saved.value;preview.value=undefined;unknown.value=false;pending=undefined;
  await run(async(signal,current)=>{
    const root=await getKnowledgeBase({knowledgeBaseId:base.value},signal);if(!current())return;
    if(root.dataClassification!=='restricted'){clear();state.value='restricted';return;}
    const value=await previewFeedbackFaq({knowledgeBaseId:base.value,body:{feedbackId:props.feedback.feedbackId}},signal);if(!current())return;
    if(value.feedback.feedbackVersion!==props.feedback.feedbackVersion||value.feedback.reviewVersion!==props.feedback.reviewVersion){unavailable();return;}
    preview.value=value;saved.value=prior?.sourceHash===value.sourceHash?prior:undefined;
  });
}
async function save(){
  if(!pending){const value=preview.value;if(!value||!form.question.trim()||!form.category.trim()){state.value='invalid';return;}
    pending={request:{knowledgeBaseId:base.value,body:{requestKey:key(),feedbackId:props.feedback.feedbackId,digest:value.digest,content:{question:form.question.trim(),aliases:form.aliases.split(/\r?\n/).map(v=>v.trim()).filter(Boolean),category:form.category.trim(),validFromUtc:form.from.trim()||null,expiresAtUtc:form.until.trim()||null}}},
      expected:{sourceHash:value.sourceHash,feedback:value.feedback,sources:value.sources,graph:value.graph}};
  }
  const frozen=pending;await run(async(signal,current)=>{const value=await createFeedbackFaq(frozen.request,frozen.expected,signal);if(!current())return;saved.value=value.workspace;unknown.value=false;pending=undefined;state.value='saved';},true);
}
watch(base,()=>{stop();clear();state.value=undefined;},{flush:'sync'});
watch(()=>[session.currentUser,session.currentContextName,session.switching,permitted.value,props.feedback.feedbackId,props.feedback.feedbackVersion,props.feedback.reviewVersion,props.feedback.reviewStateKey],close,{deep:true,flush:'sync'});
watch(()=>props.disabled,value=>{if(value)close();});onUnmounted(close);
</script>
<template>
  <ElButton v-if="permitted" data-testid="faq-feedback-open" :data-feedback-id="feedback.feedbackId" :disabled="disabled" @click="show">{{ text('open') }}</ElButton>
  <Teleport to="body"><section :aria-label="text('title')">
    <ArtFormDialog :append-to-body="false" :trap-focus="true" :open="open" :title="text('title')" :show-confirm="false" :cancel-label="text('close')" width="850px" class="faq-feedback-dialog" data-testid="faq-feedback-dialog" @update:open="close">
      <ElAlert :title="text('hint')" type="info" :closable="false" />
      <ElAlert v-if="state" :title="text(state)" :type="state==='saved'?'success':'warning'" :closable="false" :data-testid="'faq-feedback-'+state" />
      <template v-if="open&&permitted">
        <ElForm label-position="top" @submit.prevent="save">
          <ElFormItem :label="text('target')"><AiKnowledgeBaseSelect v-model="base" :teleported="false" :readonly="!!saved" :disabled="busy||unknown" /></ElFormItem>
          <ElButton data-testid="faq-feedback-preview" :disabled="busy||!base" @click="read">{{ text('preview') }}</ElButton>
          <template v-if="preview||unknown">
            <ElFormItem :label="text('question')"><ElInput v-model="form.question" :aria-label="text('question')" :maxlength="512" :disabled="busy||!!saved||unknown" data-testid="faq-feedback-question" /></ElFormItem>
            <ElFormItem :label="text('aliases')"><ElInput v-model="form.aliases" type="textarea" :aria-label="text('aliases')" :disabled="busy||!!saved||unknown" data-testid="faq-feedback-aliases" /></ElFormItem>
            <ElFormItem v-if="preview" :label="text('answer')"><ElInput :model-value="preview.answer" type="textarea" :rows="5" readonly :aria-label="text('answer')" data-testid="faq-feedback-answer" /></ElFormItem>
            <ElFormItem :label="text('category')"><ElInput v-model="form.category" :aria-label="text('category')" :maxlength="64" :disabled="busy||!!saved||unknown" data-testid="faq-feedback-category" /></ElFormItem>
            <ElFormItem :label="text('from')"><ElInput v-model="form.from" :aria-label="text('from')" :disabled="busy||!!saved||unknown" /></ElFormItem>
            <ElFormItem :label="text('until')"><ElInput v-model="form.until" :aria-label="text('until')" :disabled="busy||!!saved||unknown" /></ElFormItem>
            <ElButton v-if="!saved" native-type="submit" :disabled="busy" data-testid="faq-feedback-save">{{ text(unknown?'retry':'save') }}</ElButton>
          </template>
        </ElForm>
        <p v-if="busy" role="status">{{ text('loading') }}</p>
        <template v-if="preview">
          <h3>{{ text('feedback') }}</h3><p class="identity" data-testid="faq-feedback-source">{{ preview.feedback.feedbackId }} · v{{ preview.feedback.feedbackVersion }} / v{{ preview.feedback.reviewVersion }}</p>
          <h3>{{ text('sources') }}</h3><ul><li v-for="source in preview.sources" :key="source.documentId" class="identity">{{ source.knowledgeBaseId }} · {{ source.documentId }} · {{ source.versionId }} · {{ source.publicationId }}</li></ul>
          <template v-if="preview.graph"><h3>{{ text('parents') }}</h3><ul data-testid="faq-feedback-graph"><li v-for="node in preview.graph.nodes" :key="node.source.documentId" class="identity">{{ node.source.faqId }} · {{ node.source.faqRevisionId }} → {{ node.parentDocumentIds.join(' · ') }}</li></ul></template>
        </template>
        <ElButton v-if="saved" data-testid="faq-feedback-edit" @click="editOpen=true">{{ text('edit') }}</ElButton>
      </template>
    </ArtFormDialog>
  </section></Teleport>
  <AiKnowledgeFaqDraftDialog v-if="saved" v-model:open="editOpen" :knowledge-base-id="saved.knowledgeBaseId" :workspace="saved" @unavailable="unavailable" />
</template>
<style scoped>
.identity { overflow-wrap:anywhere; white-space:normal; }
:deep(.el-form) { margin-block:16px; }
:deep(.el-button) { margin-block:4px; }
:global(.faq-feedback-dialog){max-height:calc(100vh - 24px);display:flex;flex-direction:column;} :global(.faq-feedback-dialog .el-dialog__body){min-height:0;overflow-y:auto;}
</style>
