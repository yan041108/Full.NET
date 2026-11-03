<script setup lang="ts">
import { computed, defineAsyncComponent, onUnmounted, reactive, ref, watch } from 'vue';
import { ElAlert, ElButton, ElForm, ElFormItem, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeFaqContent, type AiKnowledgeFaqDocumentSelection, type AiKnowledgeFaqDraftWorkspace,
  type AiKnowledgeFaqDraftRevision, type AiKnowledgeFaqDraftMutation, type AiPersonalScheduleSourceSelection } from '@fullnet/client-contracts';
import ArtFormDialog from '../framework/art-design/components/ArtFormDialog.vue';
import AiKnowledgeBaseSelect from './AiKnowledgeBaseSelect.vue';
import AiKnowledgeSourceDialog from './AiKnowledgeSourceDialog.vue';
import * as api from '../api/ai-knowledge-faqs';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeFaqText } from '../i18n/ai-knowledge-faqs';
import { previewPersonalScheduleSources } from '../api/ai-personal-schedule-assistant';
const AiPersonalScheduleSourceDialog = defineAsyncComponent(() => import('./AiPersonalScheduleSourceDialog.vue'));
const props = defineProps<{ open: boolean; knowledgeBaseId: string; workspace?: AiKnowledgeFaqDraftWorkspace; selection?: AiKnowledgeFaqDocumentSelection; initialQuestion?: string }>();
const emit = defineEmits<{ 'update:open': [boolean]; saved: [AiKnowledgeFaqDraftWorkspace]; unavailable: [] }>();
const session = useSessionStore(); const { locale } = useAdminI18n(); const tr = (k: Parameters<typeof knowledgeFaqText>[1]) => knowledgeFaqText(locale.value, k);
const baseId = ref(''); const draftId = ref<string>(); const head = ref<AiKnowledgeFaqDraftWorkspace>(); const busy = ref(false); const state = ref<'saved'|'replayed'|'failed'|'conflict'|'invalid'|'unavailable'>();
const form = reactive({ question: '', aliases: '', answer: '', category: '', basis: '', validFrom: '', expires: '' });
const revisions = ref<AiKnowledgeFaqDraftRevision[]>([]); const afterRevision = ref<string>();
const preview = ref<AiKnowledgeFaqDraftWorkspace['sources'][number]>(); const sourceOpen = ref(false);
const scheduleId = ref(''); const capturedSchedules = ref<AiPersonalScheduleSourceSelection[]>();
const schedulePreview = ref<AiPersonalScheduleSourceSelection>();
const businessSources = computed(() => head.value?.personalSchedules ?? capturedSchedules.value ?? props.selection?.personalSchedules ?? []);
const canReadSchedules = computed(() => ['calendar.personal_schedules.read','calendar.personal_schedules.assistant_read'].every(code => session.can(code)));
const canRead = computed(() => !session.switching && session.can('ai.knowledge_bases.read') && session.can('ai.knowledge_faqs.read'));
const canWrite = computed(() => canRead.value && session.can('ai.knowledge_faqs.write'));
const sources = computed(() => head.value?.sources ?? props.selection?.sources ?? []);
const derived = computed(() => head.value ? head.value.origin !== 'independent' : props.workspace ? props.workspace.origin !== 'independent' : !!props.selection);
let epoch = 0; let controller: AbortController | undefined; let pending: { signature: string; key: string } | undefined;
// 请求键为 UUID v7；未知响应保留同一意图的键，输入变化才生成新意图。
function key() { const bytes = crypto.getRandomValues(new Uint8Array(16)); let now = BigInt(Date.now()); for (let i=5;i>=0;--i) { bytes[i]=Number(now & 255n); now >>= 8n; } bytes[6]=(bytes[6]! & 15)|112; bytes[8]=(bytes[8]! & 63)|128; const h=Array.from(bytes,b=>b.toString(16).padStart(2,'0')).join(''); return `${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`; }
function requestKey(signature: string) { if (pending?.signature !== signature) pending = { signature, key: key() }; return pending.key; }
function stop() { ++epoch; controller?.abort(); busy.value = false; preview.value = undefined; sourceOpen.value = false; schedulePreview.value = undefined; }
function fill(w?: AiKnowledgeFaqDraftWorkspace) { const c=w?.revision.content; Object.assign(form, { question:c?.question ?? props.initialQuestion ?? '',aliases:c?.aliases.join('\n') ?? '',answer:c?.answer ?? '',category:c?.category ?? '',basis:w?.basis ?? '',validFrom:c?.validFromUtc ?? '',expires:c?.expiresAtUtc ?? '' }); }
function clearSensitive() { draftId.value=undefined; head.value=undefined; revisions.value=[]; afterRevision.value=undefined; pending=undefined; capturedSchedules.value=undefined; scheduleId.value=''; Object.assign(form,{question:'',aliases:'',answer:'',category:'',basis:'',validFrom:'',expires:''}); }
function reset() { stop(); capturedSchedules.value=undefined; scheduleId.value=''; head.value=props.workspace; draftId.value=props.workspace?.id; baseId.value=props.workspace?.knowledgeBaseId ?? props.knowledgeBaseId; state.value=undefined; revisions.value=[]; afterRevision.value=undefined; pending=undefined; fill(head.value); }
function close() { stop(); clearSensitive(); emit('update:open', false); }
async function run(action: (signal: AbortSignal) => Promise<void>) {
  if (busy.value || !props.open || !canRead.value) return;
  stop(); const current=++epoch; controller=new AbortController(); const signal=controller.signal; busy.value=true; state.value=undefined;
  try { await action(signal); }
  catch (error) { if (current !== epoch) return;
    if (isFullNetProblemDetails(error) && error.code === 'ai.knowledge_faqs.version_conflict') { head.value=undefined; revisions.value=[]; state.value='conflict'; }
    else if (isFullNetProblemDetails(error) && (error.code === 'ai.knowledge_faqs.source_unavailable' || [401,403,404].includes(error.status))) {
      clearSensitive(); state.value='unavailable'; emit('unavailable');
    } else state.value='failed';
  } finally { if(current===epoch) busy.value=false; }
}
function accept(r: AiKnowledgeFaqDraftMutation, signal: AbortSignal) { if (signal.aborted || !props.open) return; head.value=r.workspace; draftId.value=r.workspace.id; baseId.value=r.workspace.knowledgeBaseId;
  fill(head.value); revisions.value=[]; afterRevision.value=undefined; pending=undefined; state.value=r.replayed?'replayed':'saved'; emit('saved',r.workspace); }
function content(): AiKnowledgeFaqContent { return {question:form.question.trim(),aliases:form.aliases.split(/\r?\n/).map(s=>s.trim()).filter(Boolean),answer:form.answer.trim(),category:form.category.trim(),validFromUtc:form.validFrom.trim()||null,expiresAtUtc:form.expires.trim()||null}; }
async function save() {
  if (!canWrite.value || state.value==='conflict' || state.value==='unavailable') return;
  if (!head.value && scheduleId.value.trim() && !capturedSchedules.value) {state.value='invalid';return;}
  const c=content(); if (!baseId.value || !c.question || !c.answer || !c.category || !derived.value && !head.value && !form.basis.trim()) { state.value='invalid';return; }
  const before=head.value; const selection=props.selection ? { ...props.selection,sources:props.selection.sources.map(s=>({...s})),
    ...(businessSources.value.length ? {personalSchedules:businessSources.value.map(value=>({...value}))} : {}) } : undefined;
  const signature=JSON.stringify(['save',baseId.value,before?.id,before?.revision.id,before?.headVersion,c,selection,form.basis]); const requestKeyValue=requestKey(signature);
  await run(async signal=> { let r: AiKnowledgeFaqDraftMutation;
    if (before) r=await api.saveFaqDraft({knowledgeBaseId:baseId.value,faqId:before.id,body:{requestKey:requestKeyValue,expectedRevisionId:before.revision.id,expectedHeadVersion:before.headVersion,content:c}},before,signal);
    else if(selection) r=await (selection.sources.some(s=>s.sourceKind==='faq-revision-v1')?api.createDerivedFaq:api.createDocumentFaq)({knowledgeBaseId:baseId.value,body:{requestKey:requestKeyValue,content:c,selection}},signal);
    else r=await api.createIndependentFaq({knowledgeBaseId:baseId.value,body:{requestKey:requestKeyValue,content:c,basis:form.basis.trim()}},signal);
    accept(r,signal);
  });
}
async function reload() {
  const id=draftId.value; if(!id)return;
  await run(async signal=>{const w=await api.getFaqDraft({knowledgeBaseId:baseId.value,faqId:id},signal); if(!signal.aborted){head.value=w;fill(w);pending=undefined;revisions.value=[];state.value=undefined;}});
}
async function captureSchedule() {
  if (!props.selection || head.value || !canWrite.value || !canReadSchedules.value || !/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(scheduleId.value.trim())) {state.value='invalid';return;}
  const id=scheduleId.value.trim(); capturedSchedules.value=undefined;
  await run(async signal=>{
    try { const result=await previewPersonalScheduleSources([id],signal); if(signal.aborted)return;
      capturedSchedules.value=result.sources.map(value=>({id:value.id,version:value.version,projectionHash:value.projectionHash})); pending=undefined;
    } catch { if(!signal.aborted){clearSensitive();state.value='unavailable';emit('unavailable');} }
  });
}
async function history(next=false) { const before=head.value;if(!before)return;
  await run(async signal=>{const p=await api.listFaqRevisions({knowledgeBaseId:before.knowledgeBaseId,faqId:before.id,afterId:next?afterRevision.value:undefined},before,signal);if(!signal.aborted){revisions.value=next?[...revisions.value,...p.items]:[...p.items];afterRevision.value=p.nextAfterId??undefined;}});
}
async function mutate(mode:'copy'|'restore', revisionId?: string) {
  if(!canWrite.value)return; const before=head.value;if(!before)return;
  const requestKeyValue=requestKey(JSON.stringify([mode,before.id,before.revision.id,before.headVersion,revisionId]));
  await run(async signal=>{const p={knowledgeBaseId:before.knowledgeBaseId,faqId:before.id,body:{requestKey:requestKeyValue,expectedRevisionId:before.revision.id,expectedHeadVersion:before.headVersion}};
    const r=mode==='copy'?await api.copyFaqDraft(p,before,signal):await api.restoreFaqDraft({...p,body:{...p.body,revisionId:revisionId!}},before,signal); accept(r,signal);});
}
watch(()=>[props.open,props.workspace,props.knowledgeBaseId,props.selection,props.initialQuestion],()=>{ if(props.open)reset();else{stop();clearSensitive();} },{immediate:true,deep:true});
watch(()=>[session.currentUser,session.switching,canRead.value,canWrite.value,canReadSchedules.value],close,{deep:true}); onUnmounted(()=>{stop();clearSensitive();});
</script>
<template>
<Teleport to="body"><section :aria-label="tr('title')">
  <ArtFormDialog :append-to-body="false" :trap-focus="true" :open="open" :title="tr('title')" :show-confirm="false" :cancel-label="tr('close')" width="850px" class="faq-draft-dialog" data-testid="faq-dialog" @update:open="close">
    <ElAlert :title="tr('hint')" type="info" :closable="false" />
    <ElAlert v-if="state" :title="tr(state)" :type="state==='saved'||state==='replayed'?'success':'warning'" :closable="false" :data-testid="'faq-'+state" />
    <template v-if="canRead && state!=='unavailable'">
      <p v-if="head?.feedback" class="faq-feedback-identity" data-testid="faq-origin-feedback">{{ tr('feedbackSource') }} · {{ head.feedback.feedbackId }} · v{{ head.feedback.feedbackVersion }} / v{{ head.feedback.reviewVersion }}</p>
      <p data-testid="faq-state">{{ tr('draft') }} · {{ tr(head?.origin==='feedback_derived'?'feedbackDerived':head?.origin==='faq_derived'||selection?.sources.some(s=>s.sourceKind==='faq-revision-v1')?'parentDerived':derived?'derived':'independent') }} <span v-if="head">· {{ tr('revision') }} {{ head.headVersion }}</span></p>
      <ElForm label-position="top" @submit.prevent="save">
        <section v-if="selection && !head && canWrite && canReadSchedules" :aria-label="tr('businessSources')" class="faq-business-choice">
          <label for="faq-schedule-id">{{tr('scheduleId')}}</label><ElInput id="faq-schedule-id" v-model="scheduleId" :disabled="busy" data-testid="faq-schedule-id" />
          <ElButton :disabled="busy" data-testid="faq-capture-schedule" @click="captureSchedule">{{tr('captureSchedule')}}</ElButton>
        </section>
        <section v-if="businessSources.length" :aria-label="tr('businessSources')" data-testid="faq-business-sources"><h3>{{tr('businessSources')}}</h3><p>{{tr('businessHint')}}</p>
          <p v-for="source in businessSources" :key="source.id" class="identity">{{source.id}} · {{tr('revision')}} {{source.version}}
            <ElButton :disabled="busy || !canReadSchedules" @click="schedulePreview=source">{{tr('previewSchedule')}}</ElButton></p>
        </section>
        <ElFormItem :label="tr('target')"><AiKnowledgeBaseSelect v-model="baseId" :readonly="!!draftId" :disabled="busy || state==='conflict'" :teleported="false" /></ElFormItem>
        <ElFormItem :label="tr('question')"><ElInput v-model="form.question" :aria-label="tr('question')" :maxlength="512" :disabled="busy || !canWrite" data-testid="faq-question" /></ElFormItem>
        <ElFormItem :label="tr('aliases')"><ElInput v-model="form.aliases" type="textarea" :aria-label="tr('aliases')" :disabled="busy || !canWrite" data-testid="faq-aliases" /></ElFormItem>
        <ElFormItem :label="tr('answer')"><ElInput v-model="form.answer" type="textarea" :rows="5" :aria-label="tr('answer')" :maxlength="8000" :disabled="busy || !canWrite" data-testid="faq-answer" /></ElFormItem>
        <ElFormItem :label="tr('category')"><ElInput v-model="form.category" :aria-label="tr('category')" :maxlength="64" :disabled="busy || !canWrite" data-testid="faq-category" /></ElFormItem>
        <ElFormItem v-if="!derived" :label="tr('basis')"><ElInput v-model="form.basis" type="textarea" :aria-label="tr('basis')" :maxlength="1024" :disabled="busy || !!head || !canWrite" data-testid="faq-basis" /></ElFormItem>
        <ElFormItem :label="tr('validFrom')"><ElInput v-model="form.validFrom" :aria-label="tr('validFrom')" placeholder="2026-10-29T00:00:00Z" :disabled="busy || !canWrite" /></ElFormItem>
        <ElFormItem :label="tr('expires')"><ElInput v-model="form.expires" :aria-label="tr('expires')" placeholder="2026-12-31T00:00:00Z" :disabled="busy || !canWrite" /></ElFormItem>
        <ElButton v-if="canWrite" native-type="submit" :disabled="busy || state==='conflict'" data-testid="faq-save">{{ tr('save') }}</ElButton>
      </ElForm>
      <p v-if="busy" role="status">{{ tr('loading') }}</p>
      <div class="faq-actions"><ElButton v-if="head || state==='conflict'" :disabled="busy" data-testid="faq-reload" @click="reload">{{ tr('reload') }}</ElButton>
        <ElButton v-if="head" :disabled="busy" data-testid="faq-history" @click="history()">{{ tr('history') }}</ElButton>
        <ElButton v-if="head && canWrite" :disabled="busy" data-testid="faq-copy" @click="mutate('copy')">{{ tr('copy') }}</ElButton></div>
      <section v-if="sources.length" :aria-label="tr('sources')"><h3>{{ tr('sources') }}</h3><ul><li v-for="source in sources" :key="source.documentId" class="identity" data-testid="faq-source">
        {{ source.documentId }} · {{ source.versionId }} · {{ source.publicationId }}
        <ElButton :disabled="busy" data-testid="faq-preview" @click="preview=source;sourceOpen=true">{{ tr('preview') }}</ElButton></li></ul></section>
      <section v-if="head?.graph" data-testid="faq-parent-graph"><h3>{{tr('parents')}}</h3><ul><li v-for="node in head.graph.nodes" :key="node.source.documentId" class="identity">
        {{node.source.faqId}} · {{node.source.faqRevisionId}} · {{node.source.publicationId}}<p>{{tr('parentEdges')}}: {{node.parentDocumentIds.join(', ')||tr('none')}}</p>
      </li></ul></section>
      <article v-for="r in revisions" :key="r.id" data-testid="faq-revision"><h3>{{ tr('revision') }} {{ r.number }} · {{ r.content.question }}</h3><pre>{{ r.content.answer }}</pre>
        <ElButton v-if="canWrite" :disabled="busy" data-testid="faq-restore" @click="mutate('restore',r.id)">{{ tr('restore') }}</ElButton></article>
      <ElButton v-if="afterRevision" :disabled="busy" @click="history(true)">{{ tr('next') }}</ElButton>
    </template>
  </ArtFormDialog></section></Teleport>
  <AiKnowledgeSourceDialog v-if="preview" v-model:open="sourceOpen" :knowledge-base-id="preview.knowledgeBaseId" :document-id="preview.documentId" :version-id="preview.versionId" :source="preview" @unavailable="clearSensitive();state='unavailable';emit('unavailable')" />
  <AiPersonalScheduleSourceDialog v-if="schedulePreview" :schedule-id="schedulePreview.id" :expected-version="schedulePreview.version" :expected-hash="schedulePreview.projectionHash" :allow-ask="false"
    @close="schedulePreview=undefined" @invalidated="stop();clearSensitive();state='unavailable';emit('unavailable')" />
</template>
<style scoped>
.faq-feedback-identity { overflow-wrap:anywhere; } .identity, pre { overflow-wrap:anywhere;white-space:pre-wrap;font:inherit; } .faq-actions{display:flex;flex-wrap:wrap;gap:8px;margin-block:12px;} .faq-actions :deep(.el-button + .el-button){margin-left:0;}
.faq-business-choice { display:grid;gap:8px;margin-block:12px; } .faq-business-choice .el-button { width:max-content;max-width:100%;white-space:normal;height:auto;padding-block:8px; }
:global(.faq-draft-dialog){max-height:calc(100vh - 24px);display:flex;flex-direction:column;} :global(.faq-draft-dialog .el-dialog__body){min-height:0;overflow-y:auto;}
</style>
