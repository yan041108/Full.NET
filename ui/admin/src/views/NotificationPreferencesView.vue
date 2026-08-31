<script setup lang="ts">
import { ElCard } from 'element-plus';
import { useAdminI18n } from '../i18n/adminI18n';

defineOptions({ name: 'NotificationPreferencesView' });

/** 偏好 API 尚未交付；即使拥有 update/manage 也不得伪造编辑入口。 */
const { t } = useAdminI18n();
</script>

<template>
  <section class="notification-preferences-view art-page-stack" data-testid="notification-preferences-unavailable">
    <header class="art-page-header">
      <p class="art-eyebrow">{{ t('notificationPreferences.eyebrow') }}</p>
      <h1 data-route-heading tabindex="-1">{{ t('notificationPreferences.title') }}</h1>
    </header>
    <ElCard shadow="never">
      <p>{{ t('notificationPreferences.unavailable') }}</p>
    </ElCard>
  </section>
</template>
