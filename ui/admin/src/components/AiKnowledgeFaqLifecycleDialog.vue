<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { ElAlert, ElButton, ElCheckbox, ElDialog, ElInput } from 'element-plus';
import { isFullNetProblemDetails, type AiKnowledgeFaqLifecycle, type AiKnowledgeFaqPublication, type AiKnowledgeFaqPublishedContent } from '@fullnet/client-contracts';
import * as api from '../api/ai-knowledge-faq-lifecycle';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { faqLifecycleText } from '../i18n/ai-knowledge-faq-lifecycle';
const props=defineProps<{open:boolean;knowledgeBaseId:string;faqId:string;publicationId?:string}>();
const emit=defineEmits<{'update:open':[boolean];unavailable:[]}>();
const session=useSessionStore();const{locale}=useAdminI18n();const tr=(k:Parameters<typeof faqLifecycleText>[1])=>faqLifecycleText(locale.value,k);
const lifecycle=ref<AiKnowledgeFaqLifecycle>();const proposal=ref<AiKnowledgeFaqPublication>();const published=ref<AiKnowledgeFaqPublishedContent>();
const busy=ref(false);const confirmed=ref(false);const from=ref('');const until=ref('');const notice=ref<'saved'|'failed'|'unavailable'|'invalid'>();
const closing=ref<{publicationId:string;publicationVersion:number}>();
const canRead=computed(()=>!session.switching&&['ai.knowledge_bases.read','ai.knowledge_faqs.read','ai.knowledge_documents.read','ai.knowledge_documents.source_read','ai.knowledge_documents.index_read'].every(session.can));
const canIndex=computed(()=>canRead.value&&session.can('ai.knowledge_faqs.index')&&session.can('ai.knowledge_documents.upload')&&session.can('ai.knowledge_documents.index_enqueue'));
const canGovern=computed(()=>canRead.value&&session.can('ai.knowledge_faqs.publish'));
const canRequest=computed(()=>canGovern.value&&session.can('ai.knowledge_publications.request'));
const canPublish=computed(()=>canGovern.value&&session.can('ai.knowledge_publications.publish'));
const canWithdraw=computed(()=>canGovern.value&&session.can('ai.knowledge_publications.withdraw'));
const canReview=computed(()=>canRead.value&&session.can('ai.knowledge_faqs.review')&&session.can('ai.knowledge_publications.review'));
const self=computed(()=>proposal.value?.requestedByUserId===session.currentUser?.id);
const link=computed(()=>proposal.value?`${window.location.origin}/#/ai/knowledge-faqs?knowledgeBaseId=${encodeURIComponent(props.knowledgeBaseId)}&faqId=${encodeURIComponent(props.faqId)}&publicationId=${encodeURIComponent(proposal.value.id)}`:'');
const params=()=>({knowledgeBaseId:props.knowledgeBaseId,faqId:props.faqId});
let epoch=0;let controller:AbortController|undefined;let timer:ReturnType<typeof setTimeout>|undefined;let intent:{signature:string;key:string}|undefined;
// 未确认的收费准入沿用原请求键；读状态轮询绝不重新派发写操作。
function key(signature:string){if(intent?.signature!==signature){const b=crypto.getRandomValues(new Uint8Array(16));let n=BigInt(Date.now());for(let i=5;i>=0;--i){b[i]=Number(n&255n);n>>=8n;}b[6]=(b[6]!&15)|112;b[8]=(b[8]!&63)|128;const h=Array.from(b,x=>x.toString(16).padStart(2,'0')).join('');intent={signature,key:`${h.slice(0,8)}-${h.slice(8,12)}-${h.slice(12,16)}-${h.slice(16,20)}-${h.slice(20)}`};}return intent.key;}
function stop(){++epoch;controller?.abort();clearTimeout(timer);busy.value=false;}
function clear(){lifecycle.value=undefined;proposal.value=undefined;published.value=undefined;from.value='';until.value='';confirmed.value=false;intent=undefined;closing.value=undefined;}
function close(){stop();clear();emit('update:open',false);}
async function run(action:(signal:AbortSignal)=>Promise<void>){if(busy.value||!props.open||!canRead.value)return;stop();const current=++epoch;controller=new AbortController();busy.value=true;notice.value=undefined;
  try{await action(controller.signal);if(current===epoch)notice.value='saved';}
  catch(error){if(current!==epoch)return;published.value=undefined;proposal.value=undefined;
    if(isFullNetProblemDetails(error)&&(error.code==='ai.knowledge_faqs.source_unavailable'||[401,403,404].includes(error.status))){const known=error.code==='ai.knowledge_faqs.source_unavailable'?closing.value:undefined;clear();closing.value=known;notice.value='unavailable';emit('unavailable');}
    else{lifecycle.value=undefined;notice.value='failed';}}
  finally{if(current===epoch)busy.value=false;}}
async function read(signal:AbortSignal){if(props.publicationId){const p=await api.getFaqPublication({...params(),publicationId:props.publicationId},signal);if(!signal.aborted)proposal.value=p;return;}
  const l=await api.getFaqLifecycle(params(),signal);if(signal.aborted)return;lifecycle.value=l;closing.value=l.publicationId?{publicationId:l.publicationId,publicationVersion:l.publicationVersion}:undefined;
  if(l.pendingPublicationId&&session.can('ai.knowledge_publications.read')&&(canGovern.value||canReview.value)){const p=await api.getFaqPublication({...params(),publicationId:l.pendingPublicationId},signal);if(!signal.aborted)proposal.value=p;}
  if(['pending','pending_processing','queued','processing','running'].includes(l.indexState))timer=setTimeout(()=>void refresh(),2000);}
async function refresh(){published.value=undefined;proposal.value=undefined;await run(read);}
async function index(){const l=lifecycle.value;if(!l||!confirmed.value||!canIndex.value)return;const body={requestKey:key(JSON.stringify(['index',params(),l.revisionId,l.headVersion])),expectedRevisionId:l.revisionId,expectedHeadVersion:l.headVersion,confirmCost:true};
  await run(async signal=>{await api.indexFaq({...params(),body},signal);if(!signal.aborted)await read(signal);});}
async function request(){const l=lifecycle.value;if(!l?.versionId||l.indexState!=='ready'||!canRequest.value)return;
  if(!/(Z|[+-]\d\d:\d\d)$/.test(from.value)||!/(Z|[+-]\d\d:\d\d)$/.test(until.value)||!Number.isFinite(Date.parse(from.value))||!Number.isFinite(Date.parse(until.value))||Date.parse(until.value)<=Date.parse(from.value)){notice.value='invalid';return;}
  // 保留服务器有效期的微秒精度；经 Date 转写为毫秒可能把起点提前到修订允许范围之外。
  const frozen={expectedRevisionId:l.revisionId,versionId:l.versionId,expectedHeadVersion:l.headVersion,expectedPublicationVersion:l.publicationVersion,effectiveFromUtc:from.value.trim(),effectiveToUtc:until.value.trim()};
  const body={...frozen,requestKey:key(JSON.stringify(['request',params(),frozen]))};await run(async signal=>{const p=await api.requestFaqPublication({...params(),body},signal);if(!signal.aborted)proposal.value=p;});}
async function review(approve:boolean){const p=proposal.value;if(!p||!canReview.value||self.value||!p.current||p.state!=='pending')return;
  await run(async signal=>{const next=await api.reviewFaqPublication({...params(),publicationId:p.id,body:{approve,version:p.version,snapshotHash:p.snapshotHash}},signal);if(!signal.aborted)proposal.value=next;});}
async function publish(){const p=proposal.value;if(!p||!canPublish.value||!self.value||!p.current||p.state!=='approved')return;
  await run(async signal=>{const next=await api.publishFaqPublication({...params(),publicationId:p.id,body:{version:p.version,snapshotHash:p.snapshotHash}},signal);if(!signal.aborted){proposal.value=next;if(!props.publicationId)await read(signal);}});}
async function withdraw(){const l=closing.value;if(!l||!canWithdraw.value)return;
  await run(async signal=>{await api.withdrawFaqPublication({...params(),body:{publicationId:l.publicationId,expectedHeadVersion:l.publicationVersion}},signal);if(!signal.aborted){clear();notice.value='saved';}});}
async function preview(){await run(async signal=>{const body=await api.getPublishedFaq(params(),signal);if(!signal.aborted)published.value=body;});}
function state(value:string){return tr((value==='failed'?'failedState':value==='stale'?'staleState':value==='unavailable'?'unavailableState':value==='pending'&&proposal.value?'reviewPending':value) as Parameters<typeof tr>[0]);}
watch(()=>[props.open,props.knowledgeBaseId,props.faqId,props.publicationId],()=>{stop();clear();notice.value=undefined;if(props.open&&canRead.value)void refresh();},{immediate:true});
watch(()=>[session.currentUser,session.switching,canRead.value],()=>{stop();clear();notice.value='unavailable';},{deep:true});onUnmounted(()=>{stop();clear();});
</script>
<template><ElDialog :model-value="open" :title="tr('title')" class="faq-lifecycle-dialog" width="min(900px, 95vw)" :close-on-click-modal="false" @update:model-value="close">
  <ElAlert :title="tr('hint')" type="info" :closable="false"/>
  <ElAlert v-if="!canRead" :title="tr('unavailable')" type="error" :closable="false"/>
  <ElAlert v-if="notice" :title="tr(notice)" :type="notice==='saved'?'success':'error'" :closable="false" data-testid="faq-lifecycle-notice"/>
  <div class="actions"><ElButton :disabled="busy||!canRead" data-testid="faq-lifecycle-refresh" @click="refresh">{{tr('refresh')}}</ElButton></div>
  <ElButton v-if="!lifecycle&&closing&&canWithdraw" :disabled="busy" data-testid="faq-withdraw-unavailable" @click="withdraw">{{tr('withdraw')}}</ElButton>
  <section v-if="lifecycle" data-testid="faq-lifecycle-status"><p>{{tr('indexState')}}: {{state(lifecycle.indexState)}}</p><p>{{tr('publicationState')}}: {{state(lifecycle.publicationState)}} · {{tr('head')}} {{lifecycle.publicationVersion}}</p>
    <p v-if="['stale','failed','cancelled'].includes(lifecycle.indexState)" role="status">{{tr('retryRevision')}}</p><template v-if="canIndex&&!lifecycle.taskId&&lifecycle.indexState==='pending_processing'"><ElCheckbox v-model="confirmed" :disabled="busy" data-testid="faq-index-confirm">{{tr('confirm')}}</ElCheckbox><ElButton :disabled="busy||!confirmed" data-testid="faq-index" @click="index">{{tr('index')}}</ElButton></template>
    <template v-if="canRequest&&lifecycle.indexState==='ready'"><label for="faq-effective-from">{{tr('from')}}</label><ElInput id="faq-effective-from" v-model="from" :disabled="busy" placeholder="2026-10-29T08:00:00Z" data-testid="faq-effective-from"/><label for="faq-effective-until">{{tr('until')}}</label><ElInput id="faq-effective-until" v-model="until" :disabled="busy" placeholder="2026-11-29T08:00:00Z" data-testid="faq-effective-until"/><ElButton :disabled="busy" data-testid="faq-request-publication" @click="request">{{tr('request')}}</ElButton></template>
    <div class="actions"><ElButton v-if="canWithdraw&&lifecycle.publicationId" :disabled="busy" data-testid="faq-withdraw" @click="withdraw">{{tr('withdraw')}}</ElButton><ElButton v-if="lifecycle.publicationState==='published'" :disabled="busy" data-testid="faq-published-preview" @click="preview">{{tr('preview')}}</ElButton></div>
  </section>
  <section v-if="proposal" data-testid="faq-publication-snapshot"><h3>{{tr('proposal')}} · {{state(proposal.state)}}</h3><p>{{tr('revision')}}: {{proposal.revisionId}}</p><p>{{proposal.effectiveFromUtc}} — {{proposal.effectiveToUtc}}</p><h4>{{proposal.content.question}}</h4><p v-for="a in proposal.content.aliases" :key="a">{{a}}</p><pre>{{proposal.content.answer}}</pre><p>{{tr('origin')}}: {{proposal.origin}}</p><p v-if="proposal.basis">{{tr('basis')}}: {{proposal.basis}}</p><h4>{{tr('sources')}}</h4><p v-for="s in proposal.sources" :key="s.documentId">{{s.documentId}} · {{s.versionId}} · {{s.publicationId}} · {{s.manifestHash}}</p>
    <label for="faq-review-link">{{tr('link')}}</label><ElInput id="faq-review-link" :model-value="link" readonly data-testid="faq-review-link"/>
    <p v-if="self">{{tr('self')}}</p><p v-if="!proposal.current" role="status">{{tr('stale')}}</p><div class="actions"><ElButton v-if="canReview" :disabled="busy||self||!proposal.current||proposal.state!=='pending'" data-testid="faq-approve" @click="review(true)">{{tr('approve')}}</ElButton><ElButton v-if="canReview" :disabled="busy||self||!proposal.current||proposal.state!=='pending'" data-testid="faq-reject" @click="review(false)">{{tr('reject')}}</ElButton><ElButton v-if="canPublish&&self" :disabled="busy||!proposal.current||proposal.state!=='approved'" data-testid="faq-publish" @click="publish">{{tr('publish')}}</ElButton></div>
  </section>
  <section v-if="published" data-testid="faq-published-body"><h3>{{tr('body')}}</h3><h4>{{published.content.question}}</h4><pre>{{published.content.answer}}</pre><p>{{published.publicationId}}</p></section>
  <template #footer><ElButton @click="close">{{tr('close')}}</ElButton></template>
</ElDialog></template>
<style scoped>.actions{display:flex;flex-wrap:wrap;gap:8px;margin-block:12px}section{margin-block:16px;overflow-wrap:anywhere}label{display:block;margin-top:12px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}.el-checkbox{white-space:normal;height:auto;align-items:flex-start}:deep(.el-checkbox__label){white-space:normal;overflow-wrap:anywhere}</style>
