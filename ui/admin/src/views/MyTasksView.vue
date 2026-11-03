<script setup lang="ts">
import { computed, onActivated, onDeactivated, ref } from 'vue';
import { ElAlert } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import MyTaskSourcePanel from '../components/MyTaskSourcePanel.vue';
import { sourceReadPermissions } from '../tasks/myTasks';
defineOptions({ name: 'MyTasksView' });
const session = useSessionStore(), { t } = useAdminI18n(), active = ref(true);
const available = computed(() => active.value && !session.switching && !!session.currentUser?.tenantId && session.can('platform.my_tasks.read'));
// 权限目录只包含已装配来源；不能用超级管理员的 can() 通配语义猜测可选模块已安装。
const sources = computed(() => available.value ? [...sourceReadPermissions].filter(([, permission]) => session.currentUser!.permissions.includes(permission)).map(([source]) => source) : []);
onActivated(() => { active.value = true; }); onDeactivated(() => { active.value = false; });
</script>
<template>
  <section class="my-tasks-view art-page">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('taskCenter.title') }}</h1>
    <ElAlert v-if="!available" :title="t('taskCenter.tenantRequired')" type="info" :closable="false" />
    <template v-else>
      <ElAlert :title="t('taskCenter.description')" type="info" :closable="false" />
      <ElAlert v-if="sources.length === 0" :title="t('taskCenter.noSources')" type="info" :closable="false" />
      <MyTaskSourcePanel v-for="source in sources" :key="source" :source="source" />
    </template>
  </section>
</template>
<style scoped>.my-tasks-view { display: flex; flex-direction: column; gap: 16px; min-width: 0; }</style>
