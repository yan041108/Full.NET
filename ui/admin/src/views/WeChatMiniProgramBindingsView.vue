<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {
  ElAlert,
  ElButton,
  ElCard,
  ElPagination,
  ElTag
} from 'element-plus';
import { useSessionStore } from '../auth/session';
import { useAdminI18n } from '../i18n/adminI18n';
import PermissionGate from '../components/PermissionGate.vue';
import {
  listWeChatMiniProgramBindings,
  type WeChatMiniProgramBindingResponse
} from '../api/wechat-miniprogram-bindings';

defineOptions({ name: 'WeChatMiniProgramBindingsView' });

/** 首切片只提供绑定可观测与 AppId 核对；OpenId 交换与订阅登记由小程序端调用受保护 API 完成。 */
const session = useSessionStore();
const { t } = useAdminI18n();
const items = ref<WeChatMiniProgramBindingResponse[]>([]);
const selected = ref<WeChatMiniProgramBindingResponse>();
const page = ref(1);
const pageSize = ref(20);
const total = ref(0);
const loading = ref(false);
const errorMessage = ref<string>();
const canRead = computed(() => session.can('notifications.wechat_miniprogram_bindings.read'));

onMounted(load);

async function load(): Promise<void> {
  if (!canRead.value) {
    return;
  }
  loading.value = true;
  errorMessage.value = undefined;
  try {
    const result = await listWeChatMiniProgramBindings(page.value, pageSize.value);
    items.value = result.items;
    page.value = result.page;
    pageSize.value = result.pageSize;
    total.value = result.total;
  } catch {
    errorMessage.value = t('wechatMiniProgramBindings.loadFailed');
  } finally {
    loading.value = false;
  }
}

function selectItem(item: WeChatMiniProgramBindingResponse): void {
  selected.value = item;
}

function subscriptionTagType(statusKey: string): 'success' | 'info' | 'warning' {
  if (statusKey === 'accepted') {
    return 'success';
  }
  if (statusKey === 'rejected') {
    return 'warning';
  }
  return 'info';
}
</script>

<template>
  <section class="wechat-miniprogram-bindings-view art-page-stack art-full-height" :aria-busy="loading">
    <header class="art-page-header">
      <h1>{{ t('wechatMiniProgramBindings.title') }}</h1>
      <p>{{ t('wechatMiniProgramBindings.caption') }}</p>
    </header>

    <ElAlert
      v-if="errorMessage"
      type="error"
      :closable="false"
      show-icon
      :title="errorMessage"
    />

    <PermissionGate permission="notifications.wechat_miniprogram_bindings.read">
      <div class="art-split-layout">
        <ElCard class="art-card-list" shadow="never">
          <template #header>
            <span>{{ t('wechatMiniProgramBindings.listTitle') }}</span>
          </template>
          <div v-if="loading" class="art-muted">{{ t('common.loading') }}</div>
          <div v-else-if="items.length === 0" class="art-muted" data-testid="wechat-miniprogram-bindings-empty">
            {{ t('wechatMiniProgramBindings.empty') }}
          </div>
          <ul v-else class="art-list">
            <li v-for="item in items" :key="item.id">
              <button
                type="button"
                data-testid="wechat-miniprogram-bindings-item"
                :class="{ 'is-active': selected?.id === item.id }"
                @click="selectItem(item)"
              >
                <strong>{{ item.appId }}</strong>
                <span>{{ item.openIdMask }}</span>
              </button>
            </li>
          </ul>
          <ElPagination
            v-if="total > pageSize"
            layout="prev, pager, next"
            :total="total"
            :page-size="pageSize"
            :current-page="page"
            @current-change="(value: number) => { page = value; void load(); }"
          />
        </ElCard>

        <ElCard v-if="selected" class="art-card-detail" shadow="never" data-testid="wechat-miniprogram-bindings-detail">
          <template #header>
            <span>{{ t('wechatMiniProgramBindings.detailTitle') }}</span>
          </template>
          <dl class="art-detail-grid">
            <dt>{{ t('wechatMiniProgramBindings.fieldAppId') }}</dt>
            <dd data-testid="wechat-miniprogram-bindings-app-id">{{ selected.appId }}</dd>
            <dt>{{ t('wechatMiniProgramBindings.fieldOpenId') }}</dt>
            <dd>{{ selected.openIdMask }}</dd>
            <dt>{{ t('wechatMiniProgramBindings.fieldStatus') }}</dt>
            <dd>
              <ElTag>{{ selected.verificationStatusKey }}</ElTag>
            </dd>
          </dl>
          <section>
            <h3>{{ t('wechatMiniProgramBindings.subscriptionsTitle') }}</h3>
            <p v-if="selected.subscriptions.length === 0" class="art-muted">
              {{ t('wechatMiniProgramBindings.subscriptionsEmpty') }}
            </p>
            <ul v-else class="art-list">
              <li v-for="subscription in selected.subscriptions" :key="subscription.templateId">
                <span>{{ subscription.templateId }}</span>
                <ElTag :type="subscriptionTagType(subscription.statusKey)">
                  {{ subscription.statusKey }}
                </ElTag>
              </li>
            </ul>
          </section>
        </ElCard>
      </div>
    </PermissionGate>
  </section>
</template>
