<script setup lang="ts">
import {computed} from 'vue';
import {ElCard} from 'element-plus';
import {useSessionStore} from '../auth/session';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeEvaluationText} from '../i18n/ai-knowledge-evaluation';
import AiKnowledgeEvaluationWorkbench from '../components/AiKnowledgeEvaluationWorkbench.vue';
const session=useSessionStore();const {locale}=useAdminI18n();
const canRead=computed(()=>!session.switching&&session.can('ai.knowledge_bases.read')&&session.can('ai.knowledge_evaluation_sets.read'));
</script>
<template><ElCard><h2>{{knowledgeEvaluationText(locale,'title')}}</h2><AiKnowledgeEvaluationWorkbench v-if="canRead"/></ElCard></template>
