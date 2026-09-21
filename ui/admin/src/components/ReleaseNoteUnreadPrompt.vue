<script setup lang="ts">
import { onMounted, ref, watch } from 'vue';
import { ElButton, ElDialog } from 'element-plus';
import type { MyReleaseNote } from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { getLatestUnreadReleaseNote, markMyReleaseNoteRead } from '../api/my-release-notes';

const session = useSessionStore();
const { t, locale } = useAdminI18n();
const open = ref(false);
const loading = ref(false);
const marking = ref(false);
const note = ref<MyReleaseNote | null>(null);

watch(
  () => session.isAuthenticated,
  authenticated => {
    if (authenticated) {
      void refresh();
      return;
    }
    open.value = false;
    note.value = null;
  }
);

onMounted(() => {
  if (session.isAuthenticated) {
    void refresh();
  }
});

async function refresh(): Promise<void> {
  if (session.currentUser?.passwordChangeRequired === true) {
    return;
  }

  if (!session.can('platform.release_notes.read')) {
    return;
  }
  loading.value = true;
  try {
    const latest = await getLatestUnreadReleaseNote();
    note.value = latest;
    open.value = latest !== null;
  } catch {
    note.value = null;
    open.value = false;
  } finally {
    loading.value = false;
  }
}

function formatDateTime(value: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short'
  }).format(new Date(value));
}

async function acknowledge(): Promise<void> {
  if (!note.value || !session.can('platform.release_notes.mark_read')) {
    open.value = false;
    return;
  }
  marking.value = true;
  try {
    await markMyReleaseNoteRead(note.value.id);
    open.value = false;
    note.value = null;
  } catch {
    open.value = false;
  } finally {
    marking.value = false;
  }
}

function dismiss(): void {
  open.value = false;
}
</script>

<template>
  <el-dialog
    v-model="open"
    :title="t('releaseNotePrompt.title')"
    width="560px"
    append-to-body
    destroy-on-close
    data-testid="release-note-unread-dialog"
    @close="dismiss"
  >
    <template v-if="note">
      <p class="release-note-prompt__meta">
        <span translate="no">v{{ note.versionLabel }}</span>
        ·
        {{ formatDateTime(note.publishedAtUtc) }}
      </p>
      <h3 class="release-note-prompt__heading" translate="no">{{ note.title }}</h3>
      <pre class="release-note-prompt__content" translate="no">{{ note.content }}</pre>
    </template>
    <template #footer>
      <el-button @click="dismiss">{{ t('releaseNotePrompt.dismiss') }}</el-button>
      <el-button
        v-if="session.can('platform.release_notes.mark_read')"
        type="primary"
        data-testid="release-note-unread-mark-read"
        :loading="marking || loading"
        @click="acknowledge"
      >
        {{ t('releaseNotePrompt.markRead') }}
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.release-note-prompt__meta {
  margin: 0 0 8px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.release-note-prompt__heading {
  margin: 0 0 12px;
  font-size: 18px;
}

.release-note-prompt__content {
  margin: 0;
  max-height: 320px;
  overflow: auto;
  white-space: pre-wrap;
  word-break: break-word;
  font-family: inherit;
  line-height: 1.6;
}
</style>
