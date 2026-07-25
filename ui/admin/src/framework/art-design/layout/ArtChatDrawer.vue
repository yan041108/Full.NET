<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { Close, Paperclip, Picture } from '@element-plus/icons-vue';
import { ElAvatar, ElButton, ElDrawer, ElIcon, ElInput } from 'element-plus';

defineOptions({ name: 'ArtChatDrawer' });

defineProps<{
  title: string;
  onlineLabel: string;
  offlineLabel: string;
  inputPlaceholder: string;
  sendLabel: string;
  closeLabel: string;
}>();

const open = defineModel<boolean>('open', { default: false });

interface ChatMessage {
  id: number;
  sender: string;
  content: string;
  time: string;
  isMe: boolean;
  avatarText: string;
  avatarColor: string;
}

const BOT_NAME = 'Art Bot';
const USER_NAME = 'Admin';
const messageText = ref('');
const messageId = ref(10);
const messageContainer = ref<HTMLElement | null>(null);
const isOnline = ref(true);
const isMobile = ref(false);

const messages = ref<ChatMessage[]>([
  {
    id: 1,
    sender: BOT_NAME,
    content: '你好！我是你的 AI 助手，有什么我可以帮你的吗？',
    time: '10:00',
    isMe: false,
    avatarText: 'A',
    avatarColor: '#409eff'
  },
  {
    id: 2,
    sender: USER_NAME,
    content: '我想了解一下系统的使用方法。',
    time: '10:01',
    isMe: true,
    avatarText: '管',
    avatarColor: '#67c23a'
  },
  {
    id: 3,
    sender: BOT_NAME,
    content: '好的，我来为您介绍系统的主要功能。首先，您可以通过左侧菜单访问不同的功能模块…',
    time: '10:02',
    isMe: false,
    avatarText: 'A',
    avatarColor: '#409eff'
  },
  {
    id: 4,
    sender: USER_NAME,
    content: '听起来很不错，能具体讲讲数据分析部分吗？',
    time: '10:05',
    isMe: true,
    avatarText: '管',
    avatarColor: '#67c23a'
  },
  {
    id: 5,
    sender: BOT_NAME,
    content: '当然可以。数据分析模块可以帮助您实时监控关键指标，并生成详细的报表…',
    time: '10:06',
    isMe: false,
    avatarText: 'A',
    avatarColor: '#409eff'
  }
]);

const drawerSize = computed(() => isMobile.value ? '100%' : '480px');

function updateViewport(): void {
  isMobile.value = window.matchMedia('(max-width: 640px)').matches;
}

function formatCurrentTime(): string {
  return new Date().toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit'
  });
}

function scrollToBottom(): void {
  void nextTick(() => {
    window.setTimeout(() => {
      if (messageContainer.value) {
        messageContainer.value.scrollTop = messageContainer.value.scrollHeight;
      }
    }, 100);
  });
}

function sendMessage(): void {
  const text = messageText.value.trim();
  if (!text) {
    return;
  }

  messages.value.push({
    id: messageId.value++,
    sender: USER_NAME,
    content: text,
    time: formatCurrentTime(),
    isMe: true,
    avatarText: '管',
    avatarColor: '#67c23a'
  });
  messageText.value = '';
  scrollToBottom();
}

watch(open, value => {
  if (value) {
    scrollToBottom();
  }
});

onMounted(() => {
  updateViewport();
  window.addEventListener('resize', updateViewport);
  scrollToBottom();
});

onUnmounted(() => {
  window.removeEventListener('resize', updateViewport);
});
</script>

<template>
  <el-drawer
    v-model="open"
    :size="drawerSize"
    :with-header="false"
    append-to-body
    class="art-chat-drawer"
  >
    <div class="art-chat-drawer__header">
      <div>
        <strong>{{ title }}</strong>
        <div class="art-chat-drawer__status">
          <span :class="{ 'is-online': isOnline }" />
          <small>{{ isOnline ? onlineLabel : offlineLabel }}</small>
        </div>
      </div>
      <button type="button" class="art-chat-drawer__close" :aria-label="closeLabel" @click="open = false">
        <el-icon :size="20"><Close /></el-icon>
      </button>
    </div>

    <div class="art-chat-drawer__body">
      <div ref="messageContainer" class="art-chat-drawer__messages">
        <div
          v-for="message in messages"
          :key="message.id"
          class="art-chat-drawer__message"
          :class="{ 'is-me': message.isMe }"
        >
          <el-avatar :size="32" :style="{ background: message.avatarColor }">
            {{ message.avatarText }}
          </el-avatar>
          <div class="art-chat-drawer__bubble-wrap">
            <div class="art-chat-drawer__meta">
              <span>{{ message.sender }}</span>
              <span>{{ message.time }}</span>
            </div>
            <div class="art-chat-drawer__bubble">{{ message.content }}</div>
          </div>
        </div>
      </div>

      <div class="art-chat-drawer__composer">
        <el-input
          v-model="messageText"
          type="textarea"
          :rows="3"
          :placeholder="inputPlaceholder"
          resize="none"
          @keyup.enter.exact.prevent="sendMessage"
        />
        <div class="art-chat-drawer__actions">
          <div class="art-chat-drawer__tools">
            <el-button :icon="Paperclip" circle plain />
            <el-button :icon="Picture" circle plain />
          </div>
          <el-button type="primary" @click="sendMessage">{{ sendLabel }}</el-button>
        </div>
      </div>
    </div>
  </el-drawer>
</template>

<style scoped>
.art-chat-drawer__header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  margin-bottom: 20px;
}

.art-chat-drawer__header strong {
  font-size: 16px;
}

.art-chat-drawer__status {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-top: 6px;
}

.art-chat-drawer__status span {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--fullnet-color-danger);
}

.art-chat-drawer__status span.is-online {
  background: var(--fullnet-color-success);
}

.art-chat-drawer__status small {
  color: var(--art-gray-600);
  font-size: 12px;
}

.art-chat-drawer__close {
  display: grid;
  place-items: center;
  width: 32px;
  height: 32px;
  border: 0;
  border-radius: 8px;
  background: transparent;
  cursor: pointer;
}

.art-chat-drawer__close:hover {
  background: var(--art-hover-color);
}

.art-chat-drawer__body {
  display: flex;
  flex-direction: column;
  height: calc(100% - 70px);
}

.art-chat-drawer__messages {
  flex: 1;
  overflow-y: auto;
  padding: 16px 0;
  border-top: 1px solid var(--art-card-border);
}

.art-chat-drawer__message {
  display: flex;
  gap: 8px;
  margin-bottom: 30px;
}

.art-chat-drawer__message.is-me {
  flex-direction: row-reverse;
}

.art-chat-drawer__bubble-wrap {
  display: flex;
  flex-direction: column;
  max-width: 70%;
}

.art-chat-drawer__message.is-me .art-chat-drawer__bubble-wrap {
  align-items: flex-end;
}

.art-chat-drawer__meta {
  display: flex;
  gap: 8px;
  margin-bottom: 4px;
  font-size: 12px;
}

.art-chat-drawer__message.is-me .art-chat-drawer__meta {
  flex-direction: row-reverse;
}

.art-chat-drawer__meta span:last-child {
  color: var(--art-gray-600);
}

.art-chat-drawer__bubble {
  padding: 10px 14px;
  border-radius: 8px;
  background: rgb(0 0 0 / 6%);
  font-size: 14px;
  line-height: 1.4;
}

.art-chat-drawer__message.is-me .art-chat-drawer__bubble {
  background: rgb(64 158 255 / 15%);
}

.art-chat-drawer__composer {
  padding-top: 16px;
}

.art-chat-drawer__actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-top: 12px;
}

.art-chat-drawer__tools {
  display: flex;
  gap: 8px;
}
</style>
