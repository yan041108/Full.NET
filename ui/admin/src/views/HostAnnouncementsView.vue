<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import {
  ElButton,
  ElCard,
  ElInput,
  ElMessage,
  ElMessageBox,
  ElTag
} from 'element-plus';
import type { FullNetProblemDetails, HostAnnouncement } from '@fullnet/client-contracts';
import { isFullNetProblemDetails } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import {
  createHostAnnouncement,
  listHostAnnouncements,
  publishHostAnnouncement,
  updateHostAnnouncement
} from '../api/host-announcements';
import { useNotificationsRealtime } from '../notifications/realtime';

const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<HostAnnouncement[]>([]);
const title = ref('');
const content = ref('');
const loading = ref(false);
const changing = ref(false);
const problem = ref<FullNetProblemDetails>();
const editingId = ref<string>();
const canWrite = computed(() => session.can('notifications.announcements.write'));
const notificationsRealtime = useNotificationsRealtime();

onMounted(load);
watch(notificationsRealtime.announcementRevision, () => {
  void load();
});

async function load(): Promise<void> {
  loading.value = true;
  problem.value = undefined;
  try {
    const page = await listHostAnnouncements();
    items.value = page.items;
  } catch (error: unknown) {
    problem.value = toProblem(error);
  } finally {
    loading.value = false;
  }
}

async function create(): Promise<void> {
  if (changing.value || !title.value.trim() || !content.value.trim()) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await createHostAnnouncement(title.value.trim(), content.value.trim());
    title.value = '';
    content.value = '';
    ElMessage.success(t('hostAnnouncements.createSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

function startEdit(item: HostAnnouncement): void {
  if (item.status !== 'draft') {
    return;
  }
  editingId.value = item.id;
  title.value = item.title;
  content.value = item.content;
}

function cancelEdit(): void {
  editingId.value = undefined;
  title.value = '';
  content.value = '';
}

async function saveEdit(): Promise<void> {
  const item = items.value.find(entry => entry.id === editingId.value);
  if (!item || changing.value) {
    return;
  }
  changing.value = true;
  problem.value = undefined;
  try {
    await updateHostAnnouncement(
      item.id,
      title.value.trim(),
      content.value.trim(),
      item.version
    );
    cancelEdit();
    ElMessage.success(t('hostAnnouncements.updateSuccess'));
    await load();
  } catch (error: unknown) {
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

async function publish(item: HostAnnouncement): Promise<void> {
  if (changing.value || item.status !== 'draft') {
    return;
  }
  try {
    await ElMessageBox.confirm(
      t('hostAnnouncements.confirmPublish', { title: item.title }),
      t('hostAnnouncements.publish'),
      {
        type: 'warning',
        confirmButtonText: t('hostAnnouncements.publish'),
        cancelButtonText: t('status.back')
      }
    );
    changing.value = true;
    await publishHostAnnouncement(item.id, item.version);
    ElMessage.success(t('hostAnnouncements.publishSuccess'));
    await load();
  } catch (error: unknown) {
    if (error === 'cancel' || error === 'close') {
      return;
    }
    problem.value = toProblem(error, 'hostAnnouncements.operationFailed');
  } finally {
    changing.value = false;
  }
}

function statusLabel(status: HostAnnouncement['status']): string {
  return status === 'published'
    ? t('hostAnnouncements.statusPublished')
    : t('hostAnnouncements.statusDraft');
}

function toProblem(
  error: unknown,
  fallbackKey: 'hostAnnouncements.loadFailed' | 'hostAnnouncements.operationFailed' = 'hostAnnouncements.loadFailed'
): FullNetProblemDetails {
  return isFullNetProblemDetails(error)
    ? error
    : { status: 500, code: 'client.host_announcement_failed', title: t(fallbackKey) };
}
</script>

<template>
  <section class="host-announcements-view art-page-stack art-full-height" :aria-busy="loading">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('hostAnnouncements.title') }}</h1>

    <div v-if="problem" class="art-inline-alert" role="alert">
      <strong translate="no">{{ problem.code }}</strong>
      <span>{{ problem.title }}</span>
      <code v-if="problem.traceId" translate="no">{{ problem.traceId }}</code>
    </div>

    <el-card v-if="canWrite" shadow="never" class="art-form-card" aria-labelledby="host-announcement-form-title">
      <div><h2 id="host-announcement-form-title">{{ editingId ? t('hostAnnouncements.editTitle') : t('hostAnnouncements.createTitle') }}</h2></div>
      <label>
        <span>{{ t('hostAnnouncements.fieldTitle') }}</span>
        <el-input v-model="title" maxlength="200" />
      </label>
      <label>
        <span>{{ t('hostAnnouncements.fieldContent') }}</span>
        <el-input v-model="content" type="textarea" :rows="4" maxlength="4000" />
      </label>
      <div class="art-data-row__actions">
        <el-button v-if="editingId" plain @click="cancelEdit">{{ t('status.back') }}</el-button>
        <el-button
          type="primary"
          :loading="changing"
          :disabled="!title.trim() || !content.trim()"
          @click="editingId ? saveEdit() : create()"
        >
          {{ editingId ? t('hostAnnouncements.save') : t('hostAnnouncements.create') }}
        </el-button>
      </div>
    </el-card>

    <el-card class="art-table-card" shadow="never">
      <template #header>
        <div class="art-table-card__header">
          <h2>{{ t('hostAnnouncements.listTitle') }}</h2>
          <span class="art-table-card__count">{{ items.length }}</span>
        </div>
      </template>

      <p v-if="items.length === 0" class="art-empty-state">{{ t('hostAnnouncements.emptyList') }}</p>
      <article v-for="item in items" :key="item.id" class="art-data-row">
        <div class="art-data-row__main">
          <strong translate="no">{{ item.title }}</strong>
          <el-tag :type="item.status === 'published' ? 'success' : 'info'">{{ statusLabel(item.status) }}</el-tag>
          <p>{{ item.content }}</p>
          <small>{{ t('hostAnnouncements.createdAt') }}: {{ item.createdAtUtc }}</small>
          <small v-if="item.publishedAtUtc">{{ t('hostAnnouncements.publishedAt') }}: {{ item.publishedAtUtc }}</small>
        </div>
        <div v-if="canWrite && item.status === 'draft'" class="art-data-row__actions">
          <el-button plain :disabled="changing" @click="startEdit(item)">{{ t('hostAnnouncements.edit') }}</el-button>
          <el-button type="primary" plain :disabled="changing" @click="publish(item)">{{ t('hostAnnouncements.publish') }}</el-button>
        </div>
      </article>
    </el-card>
  </section>
</template>
