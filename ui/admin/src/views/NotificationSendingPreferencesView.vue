<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { ElAlert, ElButton, ElCard } from 'element-plus';
import type { NotificationPreferenceResponse } from '@fullnet/client-contracts';
import NotificationPreferenceEditor from '../components/NotificationPreferenceEditor.vue';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import { showProblem, showSuccess } from '../feedback/fullNetMessage';
import {
  listMyNotificationPreferences,
  listMyNotificationPreferenceChannels,
  updateMyNotificationPreference
} from '../api/notification-platform';

defineOptions({ name: 'NotificationSendingPreferencesView' });
const session = useSessionStore();
const { t } = useAdminI18n();
const preferences = ref<NotificationPreferenceResponse[]>([]);
const channels = ref<string[]>([]);
const loading = ref(false);
const savingChannel = ref<string>();
const loadFailed = ref(false);
const canRead = computed(() => session.can('notifications.preferences.read'));
const canUpdate = computed(() => session.can('notifications.preferences.update'));

onMounted(() => { void load(); });

/** 渠道以服务端描述器为准，未登记偏好按默认未同意营销显示，不伪造持久化记录。 */
async function load(): Promise<void> {
  if (!canRead.value || loading.value || savingChannel.value) return;
  loading.value = true;
  loadFailed.value = false;
  try {
    const [items, declaredChannels] = await Promise.all([
      listMyNotificationPreferences(),
      listMyNotificationPreferenceChannels()
    ]);
    preferences.value = items;
    channels.value = declaredChannels;
  } catch {
    loadFailed.value = true;
    // 块级失败只展示一次，旧数据不允许继续编辑。
    channels.value = [];
  } finally {
    loading.value = false;
  }
}

function preference(channel: string): NotificationPreferenceResponse | undefined {
  return preferences.value.find(item => item.channelKey === channel);
}

async function save(channel: string, value: {
  optedOut: boolean; marketingConsent: boolean; quietHoursJson: string | null;
}): Promise<void> {
  if (!canRead.value || !canUpdate.value || savingChannel.value || loading.value) return;
  savingChannel.value = channel;
  try {
    const updated = await updateMyNotificationPreference(channel, {
      channelKey: channel,
      channelOptedOut: value.optedOut,
      marketingConsentGranted: value.marketingConsent,
      quietHoursJson: value.quietHoursJson
    });
    preferences.value = [...preferences.value.filter(item => item.channelKey !== channel), updated];
    showSuccess(t('notificationSendingPreferences.saved'));
  } catch (error) {
    showProblem(error, t('notificationSendingPreferences.saveFailed'));
  } finally {
    savingChannel.value = undefined;
  }
}
</script>

<template>
  <section class="art-page-stack art-full-height">
    <h1 class="art-sr-heading" data-route-heading tabindex="-1">{{ t('notificationSendingPreferences.title') }}</h1>
    <ElAlert type="info" :title="t('notificationSendingPreferences.hint')" :closable="false" />
    <ElButton v-if="canRead" :loading="loading" :disabled="Boolean(savingChannel)"
      data-testid="preferences-refresh" @click="load">{{ t('notificationPreferences.refresh') }}</ElButton>
    <ElAlert v-if="loadFailed" type="error" :title="t('notificationSendingPreferences.loadFailed')" :closable="false" />
    <div class="preferences-grid">
      <ElCard v-for="channel in channels" :key="channel" shadow="never" :data-channel="channel">
        <NotificationPreferenceEditor :channel-key="channel"
          :opted-out="preference(channel)?.channelOptedOut ?? false"
          :marketing-consent="preference(channel)?.marketingConsentGranted ?? false"
          :quiet-hours-json="preference(channel)?.quietHoursJson ?? null"
          :can-update="canUpdate && canRead" :saving="Boolean(savingChannel)"
          @save="save(channel, $event)" />
      </ElCard>
    </div>
  </section>
</template>

<style scoped>
.preferences-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr));
  gap: 12px;
  overflow: auto;
  min-height: 0;
}
</style>
