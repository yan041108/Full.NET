<script setup lang="ts">
import { useAdminI18n } from '../../../i18n/adminI18n';

defineOptions({ name: 'ArtLoginLeftPanel' });

const { t } = useAdminI18n();
</script>

<template>
  <aside class="art-login-left" :aria-label="t('auth.platformDescription')">
    <div class="art-login-left__logo">
      <span class="art-login-left__mark" aria-hidden="true">F</span>
      <strong>{{ t('shell.systemName') }}</strong>
    </div>

    <div class="art-login-left__visual" aria-hidden="true" />

    <div class="art-login-left__hero">
      <h1 data-route-heading tabindex="-1">
        {{ t('auth.heroLineOne') }}<br>
        <em>{{ t('auth.heroLineTwo') }}</em>
      </h1>
      <p>{{ t('auth.heroDescription') }}</p>
    </div>

    <div class="art-login-left__decor" aria-hidden="true">
      <span />
      <span />
      <span />
      <span />
    </div>
  </aside>
</template>
