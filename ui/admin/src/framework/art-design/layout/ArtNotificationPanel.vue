<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import {
  Bell,
  ChatDotRound,
  Message,
  Star,
  User
} from '@element-plus/icons-vue';
import { ElButton } from 'element-plus';
import type { Component } from 'vue';

defineOptions({ name: 'ArtNotificationPanel' });

const props = defineProps<{
  title: string;
  markReadLabel: string;
  viewAllLabel: string;
  emptyLabel: string;
  tabNoticeLabel: string;
  tabMessageLabel: string;
  tabPendingLabel: string;
}>();

const open = defineModel<boolean>('open', { default: false });

const show = ref(false);
const visible = ref(false);
const barActiveIndex = ref(0);
let visibilityTimer: number | undefined;

type NoticeType = 'email' | 'message' | 'collection' | 'user' | 'notice';

interface NoticeItem {
  title: string;
  time: string;
  type: NoticeType;
}

interface MessageItem {
  title: string;
  time: string;
  avatarText: string;
  avatarColor: string;
}

interface PendingItem {
  title: string;
  time: string;
}

const noticeList = ref<NoticeItem[]>([
  { title: '新增国际化', time: '2024-6-13 0:10', type: 'notice' },
  { title: '冷月呆呆给你发了一条消息', time: '2024-4-21 8:05', type: 'message' },
  { title: '小肥猪关注了你', time: '2020-3-17 21:12', type: 'collection' },
  { title: '新增使用文档', time: '2024-02-14 0:20', type: 'notice' },
  { title: '小肥猪给你发了一封邮件', time: '2024-1-20 0:15', type: 'email' },
  { title: '菜单 mock 本地真实数据', time: '2024-1-17 22:06', type: 'notice' }
]);

const msgList = ref<MessageItem[]>([
  { title: '池不胖 关注了你', time: '2021-2-26 23:50', avatarText: '池', avatarColor: '#5c7cfa' },
  { title: '唐不苦 关注了你', time: '2021-2-21 8:05', avatarText: '唐', avatarColor: '#22b8cf' },
  { title: '中小鱼 关注了你', time: '2020-1-17 21:12', avatarText: '中', avatarColor: '#51cf66' },
  { title: '何小荷 关注了你', time: '2021-01-14 0:20', avatarText: '何', avatarColor: '#fcc419' },
  { title: '誶誶淰 关注了你', time: '2020-12-20 0:15', avatarText: '誶', avatarColor: '#ff6b6b' },
  { title: '冷月呆呆 关注了你', time: '2020-12-17 22:06', avatarText: '冷', avatarColor: '#845ef7' }
]);

const pendingList = ref<PendingItem[]>([]);

const barList = computed(() => [
  { name: props.tabNoticeLabel, num: noticeList.value.length },
  { name: props.tabMessageLabel, num: msgList.value.length },
  { name: props.tabPendingLabel, num: pendingList.value.length }
]);

const currentTabIsEmpty = computed(() => {
  const data = [noticeList.value, msgList.value, pendingList.value][barActiveIndex.value];
  return !data || data.length === 0;
});

const noticeStyleMap: Record<NoticeType, { icon: Component; className: string }> = {
  email: { icon: Message, className: 'is-warning' },
  message: { icon: ChatDotRound, className: 'is-success' },
  collection: { icon: Star, className: 'is-danger' },
  user: { icon: User, className: 'is-info' },
  notice: { icon: Bell, className: 'is-theme' }
};

function getNoticeStyle(type: NoticeType) {
  return noticeStyleMap[type] ?? noticeStyleMap.notice;
}

function animateOpen(value: boolean): void {
  if (visibilityTimer !== undefined) {
    window.clearTimeout(visibilityTimer);
    visibilityTimer = undefined;
  }

  if (value) {
    visible.value = true;
    show.value = true;
    return;
  }

  show.value = false;
  visibilityTimer = window.setTimeout(() => {
    visible.value = false;
    visibilityTimer = undefined;
  }, 350);
}

watch(open, value => animateOpen(value), { immediate: true });

function changeBar(index: number): void {
  barActiveIndex.value = index;
}

function closePanel(): void {
  open.value = false;
}
</script>

<template>
  <div
    v-show="visible"
    class="art-notification-panel art-card-sm"
    :class="{ 'is-open': show }"
    role="dialog"
    :aria-label="title"
    @click.stop
  >
    <div class="art-notification-panel__header">
      <span class="art-notification-panel__title">{{ title }}</span>
      <button type="button" class="art-notification-panel__mark-read">
        {{ markReadLabel }}
      </button>
    </div>

    <ul class="art-notification-panel__tabs">
      <li
        v-for="(item, index) in barList"
        :key="item.name"
        :class="{ 'is-active': barActiveIndex === index }"
        @click="changeBar(index)"
      >
        {{ item.name }} ({{ item.num }})
      </li>
    </ul>

    <div class="art-notification-panel__body">
      <div class="art-notification-panel__scroll">
        <ul v-show="barActiveIndex === 0" class="art-notification-panel__list">
          <li
            v-for="(item, index) in noticeList"
            :key="index"
            class="art-notification-panel__item"
          >
            <span
              class="art-notification-panel__icon"
              :class="getNoticeStyle(item.type).className"
            >
              <component :is="getNoticeStyle(item.type).icon" aria-hidden="true" />
            </span>
            <div>
              <h4>{{ item.title }}</h4>
              <p>{{ item.time }}</p>
            </div>
          </li>
        </ul>

        <ul v-show="barActiveIndex === 1" class="art-notification-panel__list">
          <li
            v-for="(item, index) in msgList"
            :key="index"
            class="art-notification-panel__item"
          >
            <span
              class="art-notification-panel__avatar"
              :style="{ background: item.avatarColor }"
            >
              {{ item.avatarText }}
            </span>
            <div>
              <h4>{{ item.title }}</h4>
              <p>{{ item.time }}</p>
            </div>
          </li>
        </ul>

        <ul v-show="barActiveIndex === 2" class="art-notification-panel__list">
          <li
            v-for="(item, index) in pendingList"
            :key="index"
            class="art-notification-panel__item art-notification-panel__item--plain"
          >
            <div>
              <h4>{{ item.title }}</h4>
              <p>{{ item.time }}</p>
            </div>
          </li>
        </ul>

        <div v-show="currentTabIsEmpty" class="art-notification-panel__empty">
          <Message aria-hidden="true" />
          <p>{{ emptyLabel.replace('{name}', barList[barActiveIndex]?.name ?? '') }}</p>
        </div>
      </div>

      <el-button class="art-notification-panel__view-all" @click="closePanel">
        {{ viewAllLabel }}
      </el-button>
    </div>
  </div>
</template>

<style scoped>
.art-notification-panel {
  position: absolute;
  top: calc(var(--art-header-height) + 6px);
  right: 20px;
  z-index: 80;
  width: 360px;
  max-width: calc(100vw - 24px);
  height: 500px;
  overflow: hidden;
  border: 1px solid var(--art-card-border);
  border-radius: var(--art-custom-radius, 8px);
  background: var(--art-default-box-color);
  box-shadow: var(--art-shadow-soft);
  opacity: 0;
  transform: scaleY(0.9);
  transform-origin: top;
  transition: opacity 0.3s ease, transform 0.3s ease;
  pointer-events: none;
}

.art-notification-panel.is-open {
  opacity: 1;
  transform: scaleY(1);
  pointer-events: auto;
}

.art-notification-panel__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 14px 14px 0;
}

.art-notification-panel__title {
  font-size: 16px;
  font-weight: 600;
}

.art-notification-panel__mark-read {
  padding: 4px 6px;
  border: 0;
  border-radius: 4px;
  background: transparent;
  color: var(--art-gray-700);
  font-size: 12px;
  cursor: pointer;
}

.art-notification-panel__mark-read:hover {
  background: var(--art-hover-color);
}

.art-notification-panel__tabs {
  display: flex;
  gap: 20px;
  margin: 0;
  padding: 0 14px;
  list-style: none;
  border-bottom: 1px solid var(--art-card-border);
}

.art-notification-panel__tabs li {
  height: 50px;
  overflow: hidden;
  color: var(--art-gray-700);
  font-size: 13px;
  line-height: 50px;
  cursor: pointer;
  user-select: none;
}

.art-notification-panel__tabs li.is-active {
  color: var(--art-theme-color);
  border-bottom: 2px solid var(--art-theme-color);
}

.art-notification-panel__body {
  display: flex;
  flex-direction: column;
  height: calc(100% - 95px);
}

.art-notification-panel__scroll {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
}

.art-notification-panel__list {
  margin: 0;
  padding: 0;
  list-style: none;
}

.art-notification-panel__item {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 14px;
  cursor: pointer;
}

.art-notification-panel__item:hover {
  background: rgb(0 0 0 / 4%);
}

.art-notification-panel__item h4 {
  margin: 0;
  font-size: 14px;
  font-weight: 400;
  line-height: 1.4;
}

.art-notification-panel__item p {
  margin: 6px 0 0;
  color: var(--art-gray-500);
  font-size: 12px;
}

.art-notification-panel__icon {
  display: grid;
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  place-items: center;
  border-radius: 8px;
}

.art-notification-panel__icon svg {
  width: 18px;
  height: 18px;
}

.art-notification-panel__icon.is-warning {
  background: rgb(230 162 60 / 12%);
  color: #e6a23c;
}

.art-notification-panel__icon.is-success {
  background: rgb(103 194 58 / 12%);
  color: #67c23a;
}

.art-notification-panel__icon.is-danger {
  background: rgb(245 108 108 / 12%);
  color: #f56c6c;
}

.art-notification-panel__icon.is-info {
  background: rgb(144 147 153 / 12%);
  color: #909399;
}

.art-notification-panel__icon.is-theme {
  background: rgb(64 158 255 / 12%);
  color: var(--art-theme-color);
}

.art-notification-panel__avatar {
  display: grid;
  flex-shrink: 0;
  width: 36px;
  height: 36px;
  place-items: center;
  border-radius: 8px;
  color: #fff;
  font-size: 14px;
  font-weight: 700;
}

.art-notification-panel__empty {
  padding: 80px 16px 24px;
  color: var(--art-gray-500);
  text-align: center;
}

.art-notification-panel__empty svg {
  width: 48px;
  height: 48px;
}

.art-notification-panel__empty p {
  margin: 14px 0 0;
  font-size: 12px;
}

.art-notification-panel__view-all {
  width: calc(100% - 28px);
  margin: 12px 14px 16px;
}

@media (max-width: 640px) {
  .art-notification-panel {
    top: calc(var(--art-header-height) + 2px);
    right: 0;
    width: 100%;
    max-width: 100%;
    height: 80vh;
    border-radius: 0;
  }
}
</style>
