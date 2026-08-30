<script setup lang="ts">
import type { WorkflowTodoResponse } from '@fullnet/client-contracts';
import { onShow } from '@dcloudio/uni-app';
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';

// #ifdef H5
import { h5HttpClient, h5IdentitySession, restoreH5IdentitySession } from '../../features/identity/h5-application-session';
import { createWorkflowTodoClient } from '../../features/workflow/workflow-todo-client';
import { createUniWorkflowSchemaCache } from '../../features/workflow/workflow-schema-cache';
const todoClient = createWorkflowTodoClient(h5HttpClient, createUniWorkflowSchemaCache());
// #endif

const { t } = useI18n();
const items = ref<readonly WorkflowTodoResponse[]>([]);
const loading = ref(false);
const status = ref<'ready' | 'denied' | 'failed' | 'unavailable'>('ready');

onShow(() => void load());

async function load(): Promise<void> {
  if (loading.value) return;
  loading.value = true;
  status.value = 'ready';
  try {
    // #ifdef H5
    if (h5IdentitySession.snapshot().state !== 'authenticated'
      && !await restoreH5IdentitySession()) {
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!h5IdentitySession.can('workflow.todos.read')) {
      status.value = 'denied';
      return;
    }
    items.value = await todoClient.listMine();
    // #endif
    // #ifndef H5
    status.value = 'unavailable';
    // #endif
  } catch {
    status.value = 'failed';
  } finally {
    loading.value = false;
  }
}

function openTodo(todoId: string): void {
  void uni.navigateTo({ url: `/pages/workflow/todo-detail?id=${encodeURIComponent(todoId)}` });
}
</script>

<template>
  <view class="page-shell">
    <header class="header">
      <view><text class="eyebrow">WORK QUEUE</text><text class="title">{{ t('workflow.todos.title') }}</text></view>
      <button class="refresh" :loading="loading" :disabled="loading" @click="load">{{ t('workflow.todos.refresh') }}</button>
    </header>
    <text v-if="loading" class="state">{{ t('workflow.todos.loading') }}</text>
    <text v-else-if="status === 'denied'" class="state error">{{ t('workflow.todos.denied') }}</text>
    <text v-else-if="status === 'failed'" class="state error">{{ t('workflow.todos.failed') }}</text>
    <text v-else-if="status === 'unavailable'" class="state">{{ t('identity.login.platformUnavailable') }}</text>
    <text v-else-if="items.length === 0" class="state">{{ t('workflow.todos.empty') }}</text>
    <view v-else class="list">
      <button v-for="item in items" :key="item.id" class="card" @click="openTodo(item.id)">
        <view class="card-row"><text class="step">{{ item.stepId }}</text><text class="status">{{ item.statusKey }}</text></view>
        <text class="instance">{{ item.instanceId }}</text>
        <text class="arrived">{{ t('workflow.todos.arrivedAt') }} · {{ item.arrivedAtUtc }}</text>
      </button>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 36rpx 28rpx 60rpx; background: linear-gradient(160deg, #0d2a3b, #071421 52%); }
.header, .card-row { display: flex; align-items: center; justify-content: space-between; gap: 20rpx; }
.eyebrow, .title, .instance, .arrived, .state { display: block; }
.eyebrow { color: #38d4b2; font-size: 21rpx; letter-spacing: 4rpx; }.title { margin-top: 8rpx; color: #f3fbfa; font-size: 42rpx; font-weight: 700; }
.refresh { margin: 0; padding: 0 28rpx; color: #38d4b2; background: transparent; border: 1px solid rgba(56,212,178,.45); font-size: 26rpx; }
.state { margin-top: 60rpx; padding: 40rpx; text-align: center; color: #91a7ad; }.error { color: #f28d8d; }
.list { display: grid; gap: 22rpx; margin-top: 34rpx; }.card { margin: 0; padding: 30rpx; text-align: left; color: #e7f3f2; background: rgba(12,31,48,.94); border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; }
.step { max-width: 72%; overflow: hidden; text-overflow: ellipsis; font-weight: 700; }.status { color: #38d4b2; font-size: 23rpx; }.instance, .arrived { margin-top: 14rpx; color: #91a7ad; font-size: 23rpx; overflow-wrap: anywhere; }
</style>
