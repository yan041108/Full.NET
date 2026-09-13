<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useBlobPreview } from '../../../composables/useBlobPreview';
import {
  downloadCurrentTenantBrandingLogoContent,
  getRuntimeTenantBranding
} from '@fullnet/client-contracts';
import { useAdminI18n } from '../../../i18n/adminI18n';
import { http } from '../../../api/http';

defineOptions({ name: 'ArtLoginLeftPanel' });

const { t } = useAdminI18n();
const systemTitle = ref<string | null>(null);
const logoPreview = useBlobPreview();
const logoPreviewUrl = logoPreview.url;

onMounted(() => {
  void (async () => {
    try {
      await logoPreview.load(async () => {
        const branding = await getRuntimeTenantBranding(http);
        systemTitle.value = branding.systemTitle;
        return branding.hasLogo ? downloadCurrentTenantBrandingLogoContent(http) : null;
      });
    } catch {
      systemTitle.value = null;
      logoPreview.clear();
    }
  })();
});

</script>

<template>
  <aside class="art-login-left" :aria-label="t('auth.platformDescription')">
    <div class="art-login-left__logo">
      <span v-if="logoPreviewUrl" class="art-login-left__mark art-login-left__mark--image">
        <img :src="logoPreviewUrl" alt="" />
      </span>
      <span v-else class="art-login-left__mark" aria-hidden="true">F</span>
      <strong>{{ systemTitle ?? t('shell.systemName') }}</strong>
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
