<script setup lang="ts">
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

interface PopupController {
  open(type?: 'center'): void;
  close(): void;
}

const isDevelopment = import.meta.env.DEV;
const { t } = useI18n();
const popup = ref<PopupController>();
const form = ref({ name: '' });
const submitted = ref(false);
const errorMessage = computed(() =>
  submitted.value && !form.value.name.trim() ? t('ui.smoke.required') : ''
);
const successMessage = computed(() =>
  submitted.value && !errorMessage.value ? t('ui.smoke.valid') : ''
);

function validateInput(): void {
  submitted.value = true;
}

function openPopup(): void {
  popup.value?.open('center');
}

function closePopup(): void {
  popup.value?.close();
}
</script>

<template>
  <main class="smoke-page">
    <view v-if="isDevelopment" class="smoke-panel">
      <uni-section :title="t('ui.smoke.title')" type="line">
        <view class="section-body">
          <text class="intro">{{ t('ui.smoke.intro') }}</text>

          <uni-list :border="true">
            <uni-list-item
              :title="t('ui.smoke.listTitle')"
              :note="t('ui.smoke.listNote')"
              show-arrow
            />
          </uni-list>

          <uni-forms :model-value="form" label-position="top">
            <uni-forms-item
              name="name"
              :label="t('ui.smoke.inputLabel')"
              :error-message="errorMessage"
            >
              <uni-easyinput
                v-model="form.name"
                name="name"
                :placeholder="t('ui.smoke.inputPlaceholder')"
                :aria-label="t('ui.smoke.inputLabel')"
                @input="submitted = false"
              />
            </uni-forms-item>
          </uni-forms>

          <view class="actions">
            <button
              class="action-button action-button--primary"
              type="button"
              role="button"
              tabindex="0"
              @click="validateInput"
              @keydown.enter.prevent="validateInput"
              @keydown.space.prevent="validateInput"
            >
              {{ t('ui.smoke.submit') }}
            </button>
            <button
              class="action-button"
              type="button"
              role="button"
              tabindex="0"
              @click="openPopup"
              @keydown.enter.prevent="openPopup"
              @keydown.space.prevent="openPopup"
            >
              {{ t('ui.smoke.openPopup') }}
            </button>
          </view>

          <text v-if="successMessage" class="success-message" role="status">
            {{ successMessage }}
          </text>
        </view>
      </uni-section>

      <uni-popup ref="popup" type="center">
        <view class="popup-card" role="dialog" :aria-label="t('ui.smoke.popupTitle')">
          <text class="popup-title">{{ t('ui.smoke.popupTitle') }}</text>
          <text class="popup-body">{{ t('ui.smoke.popupBody') }}</text>
          <button
            class="action-button action-button--primary"
            type="button"
            role="button"
            tabindex="0"
            @click="closePopup"
            @keydown.enter.prevent="closePopup"
            @keydown.space.prevent="closePopup"
          >
            {{ t('ui.smoke.closePopup') }}
          </button>
        </view>
      </uni-popup>
    </view>

    <view v-else class="unavailable" role="status">
      <text>{{ t('ui.smoke.unavailable') }}</text>
    </view>
  </main>
</template>

<style scoped>
.smoke-page {
  box-sizing: border-box;
  min-height: 100vh;
  padding: 16px;
  background: var(--fullnet-ui-color-canvas);
  color: var(--fullnet-ui-color-text);
}

.smoke-panel {
  width: 100%;
  max-width: 760px;
  margin: 0 auto;
  overflow: hidden;
  border: 1px solid var(--fullnet-ui-color-border);
  border-radius: var(--fullnet-ui-radius-control);
  background: var(--fullnet-ui-color-panel);
}

.section-body {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 16px;
}

.intro,
.popup-body {
  color: var(--fullnet-ui-color-text-muted);
  font-size: 14px;
  line-height: 1.6;
}

.actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
}

.action-button {
  min-height: 44px;
  margin: 0;
  padding: 0 18px;
  border: 1px solid var(--fullnet-ui-color-primary);
  border-radius: var(--fullnet-ui-radius-control);
  background: var(--fullnet-ui-color-panel);
  color: var(--fullnet-ui-color-primary);
  font-size: 15px;
}

.action-button::after {
  border: 0;
}

.action-button:focus-visible {
  outline: 3px solid var(--fullnet-ui-color-primary-bright);
  outline-offset: 3px;
}

.action-button--primary {
  background: var(--fullnet-ui-color-primary);
  color: #fffefa;
}

.success-message {
  color: var(--fullnet-ui-color-success);
  font-size: 14px;
}

.popup-card {
  display: flex;
  box-sizing: border-box;
  width: min(88vw, 420px);
  flex-direction: column;
  gap: 16px;
  padding: 24px;
  border-radius: var(--fullnet-ui-radius-control);
  background: var(--fullnet-ui-color-panel);
  color: var(--fullnet-ui-color-text);
}

.popup-title {
  font-size: 20px;
  font-weight: 700;
}

.unavailable {
  max-width: 640px;
  margin: 0 auto;
  padding: 24px;
  border-radius: var(--fullnet-ui-radius-control);
  background: var(--fullnet-ui-color-panel);
  color: var(--fullnet-ui-color-text-muted);
}

@media (max-width: 360px) {
  .smoke-page,
  .section-body {
    padding: 12px;
  }

  .actions {
    flex-direction: column;
  }

  .action-button {
    width: 100%;
  }
}
</style>
