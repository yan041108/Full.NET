<script setup lang="ts">
import { computed, onUnmounted, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import { ElAlert, ElButton, ElCard } from 'element-plus';
import type { AiKnowledgeFaqDraftWorkspace } from '@fullnet/client-contracts';
import AiKnowledgeBaseSelect from '../components/AiKnowledgeBaseSelect.vue';
import AiKnowledgeFaqDraftDialog from '../components/AiKnowledgeFaqDraftDialog.vue';
import AiKnowledgeFaqLifecycleDialog from '../components/AiKnowledgeFaqLifecycleDialog.vue';
import AiKnowledgeFaqTransferDialog from '../components/AiKnowledgeFaqTransferDialog.vue';
import { getFaqDraft, listFaqDrafts } from '../api/ai-knowledge-faqs';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { knowledgeFaqText } from '../i18n/ai-knowledge-faqs';
const session=useSessionStore(); const {locale}=useAdminI18n(); const tr=(k:Parameters<typeof knowledgeFaqText>[1])=>knowledgeFaqText(locale.value,k);
const baseId=ref('');const rows=ref<AiKnowledgeFaqDraftWorkspace[]>([]);const after=ref<string>();const busy=ref(false);const failed=ref(false);const open=ref(false);const selected=ref<AiKnowledgeFaqDraftWorkspace>();
const route=useRoute();const lifecycleOpen=ref(false);const lifecycleBase=ref('');const lifecycleFaq=ref('');const lifecyclePublication=ref<string>();
const transferOpen=ref(false);const transferMode=ref<'import'|'export'>('import');
const canRead=computed(()=>!session.switching&&session.can('ai.knowledge_faqs.read')&&session.can('ai.knowledge_bases.read'));
const canWrite=computed(()=>canRead.value&&session.can('ai.knowledge_faqs.write')); let epoch=0;let controller:AbortController|undefined;
function clear(){++epoch;controller?.abort();rows.value=[];after.value=undefined;busy.value=false;selected.value=undefined;open.value=false;lifecycleOpen.value=false;transferOpen.value=false;}
function govern(w:AiKnowledgeFaqDraftWorkspace){lifecycleBase.value=w.knowledgeBaseId;lifecycleFaq.value=w.id;lifecyclePublication.value=undefined;lifecycleOpen.value=true;}
async function load(next=false){if(!canRead.value||!baseId.value||busy.value)return;const cursor=next?after.value:undefined;clear();const current=++epoch;controller=new AbortController();busy.value=true;failed.value=false;
  try{const p=await listFaqDrafts({knowledgeBaseId:baseId.value,afterId:cursor},controller.signal);if(current===epoch){rows.value=[...p.items];after.value=p.nextAfterId??undefined;}}
  catch{if(current===epoch)failed.value=true;}finally{if(current===epoch)busy.value=false;}}
async function edit(id:string){if(!canRead.value||busy.value)return;const current=++epoch;controller?.abort();controller=new AbortController();busy.value=true;failed.value=false;selected.value=undefined;
  try{const w=await getFaqDraft({knowledgeBaseId:baseId.value,faqId:id},controller.signal);if(current===epoch){selected.value=w;open.value=true;}}
  catch{if(current===epoch){rows.value=[];failed.value=true;}}finally{if(current===epoch)busy.value=false;}}
function saved(w:AiKnowledgeFaqDraftWorkspace){rows.value=rows.value.filter(r=>r.id!==w.id);rows.value.unshift(w);}
watch(baseId,()=>{clear();void load();});watch(()=>[session.currentUser,session.switching,canRead.value],()=>{clear();baseId.value='';},{deep:true});onUnmounted(clear);
// 审核者直接读取准确申请，不借用维护者的草稿列表或所有权。
watch(()=>[route?.query,canRead.value],()=>{const q=route?.query;const valid=(v:unknown):v is string=>typeof v==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(v);
  if(canRead.value&&valid(q?.knowledgeBaseId)&&valid(q?.faqId)&&valid(q?.publicationId)){lifecycleBase.value=q.knowledgeBaseId;lifecycleFaq.value=q.faqId;lifecyclePublication.value=q.publicationId;lifecycleOpen.value=true;}
},{immediate:true,deep:true});
</script>
<template><ElCard data-testid="faq-workbench"><h2>{{tr('title')}}</h2><ElAlert :title="tr('hint')" type="info" :closable="false" />
  <ElAlert v-if="failed||!canRead" :title="tr('unavailable')" type="error" :closable="false" data-testid="faq-list-error" />
  <template v-if="canRead"><AiKnowledgeBaseSelect v-model="baseId" :disabled="busy" />
    <div class="faq-actions"><ElButton v-if="canWrite" :disabled="busy||!baseId" data-testid="faq-create" @click="selected=undefined;open=true">{{tr('create')}}</ElButton><ElButton :disabled="busy||!baseId" @click="load()">{{tr('refresh')}}</ElButton>
      <ElButton v-if="canWrite&&session.can('ai.knowledge_faqs.import')" :disabled="busy||!baseId" data-testid="faq-import" @click="transferMode='import';transferOpen=true">{{tr('import')}}</ElButton>
      <ElButton v-if="session.can('ai.knowledge_faqs.export')" :disabled="busy||!baseId||!rows.length" data-testid="faq-export" @click="transferMode='export';transferOpen=true">{{tr('export')}}</ElButton></div>
    <p v-if="busy" role="status">{{tr('loading')}}</p><p v-else-if="!baseId">{{tr('ready')}}</p><p v-else-if="!rows.length&&!failed">{{tr('empty')}}</p>
    <article v-for="w in rows" :key="w.id" data-testid="faq-list-item"><h3>{{w.revision.content.question}}</h3><p>{{tr(w.origin==='independent'?'independent':w.origin==='faq_derived'?'parentDerived':'derived')}} · {{tr('revision')}} {{w.headVersion}}</p>
      <ElButton :disabled="busy" data-testid="faq-open" @click="edit(w.id)">{{tr('load')}}</ElButton><ElButton :disabled="busy" data-testid="faq-govern" @click="govern(w)">{{tr('govern')}}</ElButton></article>
    <ElButton v-if="after" :disabled="busy" @click="load(true)">{{tr('next')}}</ElButton>
  </template></ElCard>
  <AiKnowledgeFaqDraftDialog v-model:open="open" :knowledge-base-id="baseId" :workspace="selected" @saved="saved" @unavailable="clear();failed=true" />
  <AiKnowledgeFaqLifecycleDialog v-model:open="lifecycleOpen" :knowledge-base-id="lifecycleBase" :faq-id="lifecycleFaq" :publication-id="lifecyclePublication" @unavailable="rows=[];selected=undefined;open=false;failed=true" />
  <AiKnowledgeFaqTransferDialog v-model:open="transferOpen" :knowledge-base-id="baseId" :mode="transferMode" :rows="rows" @open-faq="edit" @unavailable="rows=[];selected=undefined;open=false;failed=true" />
</template>
<style scoped>.faq-actions{display:flex;flex-wrap:wrap;gap:8px;margin-block:12px;}article{border:1px solid var(--el-border-color);border-radius:6px;padding:12px;margin-block:12px;overflow-wrap:anywhere;}</style>
