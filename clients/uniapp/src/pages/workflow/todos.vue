<script setup lang="ts">
import type { WorkflowTodoListItemResponse, WorkflowClaimPool } from '@fullnet/client-contracts';
import { onShow } from '@dcloudio/uni-app';
import { onBeforeUnmount, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import {
  httpClient,
  identitySession,
  isBusinessRuntimeAvailable,
  restoreIdentitySession
} from '../../features/identity/application-session';
import { createIdempotencyKey } from '../../features/workflow/idempotency-key';
import { classifyWorkflowTodoActionFailure } from '../../features/workflow/workflow-todo-action-failure';
import { createWorkflowTodoClient } from '../../features/workflow/workflow-todo-client';
import { createUniWorkflowSchemaCache } from '../../features/workflow/workflow-schema-cache';

const todoClient = createWorkflowTodoClient(httpClient, createUniWorkflowSchemaCache());
const { t } = useI18n();
const items = ref<readonly WorkflowTodoListItemResponse[]>([]);
const claims = ref<readonly WorkflowClaimPool[]>([]);
const queue = ref<'mine' | 'claims' | 'delegated' | 'history'>('mine');
const delegatedPage = ref(1); const delegatedTotal = ref(0);
const acting = ref<string>();
const feedback = ref('');
const pending = new Map<string, { revision: number; key: string }>();
let epoch = 0;
const unsubscribe = identitySession.subscribe(() => { epoch++; items.value = []; claims.value = []; pending.clear(); });
onBeforeUnmount(() => { epoch++; unsubscribe(); });
const loading = ref(false);
const status = ref<'ready' | 'denied' | 'failed' | 'unavailable'>('ready');

onShow(() => void load());

async function load(): Promise<void> {
  if (loading.value) {
    return;
  }
  loading.value = true;
  status.value = 'ready';
  try {
    if (!isBusinessRuntimeAvailable) {
      status.value = 'unavailable';
      return;
    }
    if (identitySession.snapshot().state !== 'authenticated'
      && !await restoreIdentitySession()) {
      await uni.reLaunch({ url: '/pages/identity/login' });
      return;
    }
    if (!identitySession.can('workflow.todos.read') && identitySession.can('workflow.claim_pools.read')) queue.value = 'claims';
    if (!identitySession.can(queue.value === 'claims' ? 'workflow.claim_pools.read' : 'workflow.todos.read')) {
      status.value = 'denied';
      return;
    }
    const current = epoch;
    if (queue.value === 'claims') {
      const result = await todoClient.listClaims(); if (current === epoch) claims.value = result;
    } else if (queue.value === 'delegated' || queue.value === 'history') {
      const result = queue.value === 'delegated' ? await todoClient.listDelegated(delegatedPage.value) : await todoClient.listHistory(delegatedPage.value);
      if (current === epoch) { items.value = result.items; delegatedPage.value = result.page; delegatedTotal.value = result.total; }
    } else {
      const result = await todoClient.listMine(); if (current === epoch) items.value = result;
    }
  } catch {
    status.value = 'failed';
  } finally {
    loading.value = false;
  }
}

async function claim(pool: WorkflowClaimPool): Promise<void> {
  if (acting.value || !identitySession.can('workflow.claim_pools.claim')) return;
  const current = epoch; acting.value = pool.id; feedback.value = '';
  try {
    let operation = pending.get(pool.id);
    if (operation?.revision !== pool.revision) { operation = { revision: pool.revision, key: createIdempotencyKey() }; pending.set(pool.id, operation); }
    const result = await todoClient.claim(pool.id, { expectedRevision: operation.revision, idempotencyKey: operation.key });
    if (current !== epoch) return;
    pending.delete(pool.id);
    if (result.activeTodoId && identitySession.can('workflow.todos.read')) openTodo(result.activeTodoId);
    await load();
  } catch (error) {
    if (current !== epoch) return;
    const failure = classifyWorkflowTodoActionFailure(error);
    if (!failure.retainIdempotencyKey) pending.delete(pool.id);
    if (failure.refreshTodo) await load();
    feedback.value = t(failure.feedbackKey);
  } finally { acting.value = undefined; }
}
function selectQueue(value: 'mine' | 'claims' | 'delegated' | 'history'): void { if (loading.value || acting.value) return; epoch++; queue.value = value; delegatedPage.value = 1; void load(); }
function changeDelegatedPage(page: number): void {
  if (loading.value || acting.value || page < 1 || (page - 1) * 20 >= delegatedTotal.value) return;
  epoch++; delegatedPage.value = page; void load();
}

function openTodo(todoId: string): void {
  void uni.navigateTo({ url: `/pages/workflow/todo-detail?id=${encodeURIComponent(todoId)}` });
}

function openInbox(): void {
  void uni.navigateTo({ url: '/pages/notifications/inbox' });
}
</script>

<template>
  <view class="page-shell">
    <header class="header">
      <view><text class="eyebrow">WORK QUEUE</text><text class="title">{{ t('workflow.todos.title') }}</text></view>
      <view class="header-actions">
        <button
          v-if="identitySession.can('notifications.inbox.read')"
          class="link"
          data-testid="workflow-open-inbox"
          @click="openInbox"
        >
          {{ t('notifications.inbox.open') }}
        </button>
        <button class="refresh" :loading="loading" :disabled="loading" @click="load">{{ t('workflow.todos.refresh') }}</button>
      </view>
    </header>
    <view class="header-actions">
      <button v-if="identitySession.can('workflow.todos.read')" :disabled="loading || !!acting" @click="selectQueue('mine')">{{ t('workflow.todos.title') }}</button>
      <button v-if="identitySession.can('workflow.todos.read')" :disabled="loading || !!acting" @click="selectQueue('delegated')">{{ t('workflow.todos.delegated') }}</button>
      <button v-if="identitySession.can('workflow.todos.read')" :disabled="loading || !!acting" @click="selectQueue('history')">{{ t('workflow.todos.history') }}</button>
      <button v-if="identitySession.can('workflow.claim_pools.read')" :disabled="loading || !!acting" @click="selectQueue('claims')">{{ t('workflow.todos.claimPools') }}</button>
    </view>
    <text v-if="feedback" class="state error">{{ feedback }}</text>
    <text v-if="loading" class="state">{{ t('workflow.todos.loading') }}</text>
    <text v-else-if="status === 'denied'" class="state error">{{ t('workflow.todos.denied') }}</text>
    <text v-else-if="status === 'failed'" class="state error">{{ t('workflow.todos.failed') }}</text>
    <text v-else-if="status === 'unavailable'" class="state">{{ t('identity.login.platformUnavailable') }}</text>
    <view v-else-if="queue === 'claims'" class="list">
      <view v-for="pool in claims" :key="pool.id" class="card">
        <text class="step">{{ pool.businessTitle || `${pool.businessType} · ${pool.businessId}` }}</text>
        <text class="arrived">{{ pool.dueAtUtc || pool.createdAtUtc }}</text>
        <button v-if="identitySession.can('workflow.claim_pools.claim')" :disabled="!!acting" :loading="acting === pool.id" @click="claim(pool)">{{ t('workflow.todos.claim') }}</button>
      </view>
      <text v-if="claims.length === 0" class="state">{{ t('workflow.todos.empty') }}</text>
    </view>
    <text v-else-if="items.length === 0" class="state">{{ t('workflow.todos.empty') }}</text>
    <view v-else class="list">
      <button v-for="item in items" :key="item.id" class="card" @click="openTodo(item.id)">
        <view class="card-row"><text class="step">{{ item.stepId }}</text><text class="status">{{ item.statusKey }}</text></view>
        <text class="instance">{{ item.instanceId }}</text>
        <text class="arrived">{{ t('workflow.todos.arrivedAt') }} · {{ item.arrivedAtUtc }}</text>
      </button>
    </view>
    <view v-if="(queue === 'delegated' || queue === 'history') && status === 'ready' && delegatedTotal > 20" class="header-actions">
      <button :disabled="loading || delegatedPage <= 1" @click="changeDelegatedPage(delegatedPage - 1)">{{ t('workflow.todo.previousPage') }}</button>
      <text>{{ delegatedPage }} / {{ Math.ceil(delegatedTotal / 20) }}</text>
      <button :disabled="loading || delegatedPage * 20 >= delegatedTotal" @click="changeDelegatedPage(delegatedPage + 1)">{{ t('workflow.todo.nextPage') }}</button>
    </view>
  </view>
</template>

<style scoped>
.page-shell { min-height: 100vh; box-sizing: border-box; padding: 36rpx 28rpx 60rpx; background: linear-gradient(160deg, #0d2a3b, #071421 52%); }
.header, .card-row, .header-actions { display: flex; align-items: center; justify-content: space-between; gap: 20rpx; }
.header-actions { flex-wrap: wrap; justify-content: flex-end; }
.eyebrow, .title, .instance, .arrived, .state { display: block; }
.eyebrow { color: #38d4b2; font-size: 21rpx; letter-spacing: 4rpx; }.title { margin-top: 8rpx; color: #f3fbfa; font-size: 42rpx; font-weight: 700; }
.refresh, .link { margin: 0; padding: 0 28rpx; color: #38d4b2; background: transparent; border: 1px solid rgba(56,212,178,.45); font-size: 26rpx; }
.state { margin-top: 60rpx; padding: 40rpx; text-align: center; color: #91a7ad; }.error { color: #f28d8d; }
.list { display: grid; gap: 22rpx; margin-top: 34rpx; }.card { margin: 0; padding: 30rpx; text-align: left; color: #e7f3f2; background: rgba(12,31,48,.94); border: 1px solid rgba(139,179,184,.18); border-radius: 20rpx; }
.step { max-width: 72%; overflow: hidden; text-overflow: ellipsis; font-weight: 700; }.status { color: #38d4b2; font-size: 23rpx; }.instance, .arrived { margin-top: 14rpx; color: #91a7ad; font-size: 23rpx; overflow-wrap: anywhere; }
</style>
