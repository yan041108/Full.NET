<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useRouter } from 'vue-router';
import { ElButton, ElCard } from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { listMyHostAnnouncements } from '../api/my-host-announcements';
import { listMyWorkflowTodos } from '../api/workflow-todos';

const session = useSessionStore();
const router = useRouter();
const { t } = useAdminI18n();
const announcements = ref<Array<{ id: string; title: string }>>([]);
const todos = ref<Array<{ id: string; title: string }>>([]);
const unavailable = ref(false);
let controller: AbortController | undefined;

/** UI 聚合各模块已授权查询，单一来源失败不吞掉其他来源，也不共享业务表或审批操作。 */
async function load(): Promise<void> {
  controller?.abort();
  const current = new AbortController();
  controller = current;
  announcements.value = [];
  todos.value = [];
  unavailable.value = false;
  const results = await Promise.allSettled([
    session.can('notifications.announcements.received.read')
      ? listMyHostAnnouncements({ page: 1, pageSize: 5 }, current.signal) : Promise.resolve(undefined),
    session.can('workflow.todos.read')
      ? listMyWorkflowTodos({ page: 1, pageSize: 5 }, current.signal) : Promise.resolve(undefined)
  ]);
  if (current.signal.aborted) return;
  const [a, b] = results;
  if (a.status === 'fulfilled' && a.value) announcements.value = a.value.items;
  if (b.status === 'fulfilled' && b.value) todos.value = b.value.items.map(item => ({ id: item.id, title: item.businessTitle || item.nodeKey }));
  unavailable.value = results.some(result => result.status === 'rejected');
}
onMounted(load);
onBeforeUnmount(() => controller?.abort());
watch(() => [session.currentUser?.id, session.currentUser?.tenantId, session.currentUser?.sessionId,
  session.can('notifications.announcements.received.read'), session.can('workflow.todos.read')], load);
</script>

<template>
  <el-card v-if="session.can('notifications.announcements.received.read') || session.can('workflow.todos.read')" class="notification-workbench">
    <h2>{{ t('inboxMessages.workbench') }}</h2>
    <p v-if="unavailable" role="status">{{ t('inboxMessages.workbenchUnavailable') }}</p>
    <div v-if="session.can('notifications.announcements.received.read')">
      <el-button @click="router.push({ name: 'my-host-announcements' })">{{ t('inboxMessages.announcements') }}</el-button>
      <ul><li v-for="item in announcements" :key="item.id" translate="no">{{ item.title }}</li></ul>
    </div>
    <div v-if="session.can('workflow.todos.read')">
      <h3>{{ t('inboxMessages.todos') }}</h3>
      <ul><li v-for="item in todos" :key="item.id"><el-button @click="router.push({ name: 'workflow-todos', query: { resourceId: item.id } })">{{ item.title }}</el-button></li></ul>
    </div>
  </el-card>
</template>
