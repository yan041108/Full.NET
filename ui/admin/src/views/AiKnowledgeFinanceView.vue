<script setup lang="ts">
import {ElCard} from 'element-plus';
import AiKnowledgeQualityPanel from '../components/AiKnowledgeQualityPanel.vue';
import {useAdminI18n} from '../i18n/adminI18n';
import {knowledgeQualityText} from '../i18n/ai-knowledge-quality';
const {locale}=useAdminI18n();
</script>
<template><ElCard><h1>{{knowledgeQualityText(locale,'title')}}</h1><AiKnowledgeQualityPanel/></ElCard></template>
