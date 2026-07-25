const BOT_NAME = 'Art Bot';
const USER_NAME = 'Admin';

const seedMessages = [
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
];

function formatCurrentTime() {
  return new Date().toLocaleTimeString([], {
    hour: '2-digit',
    minute: '2-digit'
  });
}

/**
 * Layui 顶栏聊天抽屉；结构与 Vue ArtChatDrawer 对齐，数据为壳层演示消息。
 */
export function createShellChatDrawer(root) {
  const drawer = root.querySelector('[data-shell-chat]');
  const backdrop = root.querySelector('[data-shell-chat-backdrop]');
  const openButton = root.querySelector('[data-shell-chat-open]');
  const closeButton = root.querySelector('[data-shell-chat-close]');
  const titleNode = root.querySelector('[data-shell-chat-title]');
  const statusNode = root.querySelector('[data-shell-chat-status]');
  const messagesNode = root.querySelector('[data-shell-chat-messages]');
  const inputNode = root.querySelector('[data-shell-chat-input]');
  const sendButton = root.querySelector('[data-shell-chat-send]');
  const panelNode = root.querySelector('[data-shell-chat-panel]');
  let translate = key => key;
  let isOpen = false;
  let messageId = 10;
  let messages = seedMessages.map(message => ({ ...message }));
  let isOnline = true;
  let isMobile = false;

  function updateViewport() {
    if (typeof window.matchMedia === 'function') {
      isMobile = window.matchMedia('(max-width: 640px)').matches;
    } else {
      isMobile = false;
    }
    if (panelNode) {
      panelNode.style.width = isMobile ? '100%' : '480px';
    }
  }

  function scrollToBottom() {
    window.setTimeout(() => {
      if (messagesNode) {
        messagesNode.scrollTop = messagesNode.scrollHeight;
      }
    }, 100);
  }

  function renderMessages() {
    if (!messagesNode) {
      return;
    }

    const ownerDocument = messagesNode.ownerDocument;
    const fragment = ownerDocument.createDocumentFragment();
    messages.forEach(message => {
      const row = ownerDocument.createElement('div');
      row.className = 'fn-chat-drawer__message';
      if (message.isMe) {
        row.classList.add('is-me');
      }

      const avatar = ownerDocument.createElement('span');
      avatar.className = 'fn-chat-drawer__avatar';
      avatar.style.background = message.avatarColor;
      avatar.textContent = message.avatarText;

      const wrap = ownerDocument.createElement('div');
      wrap.className = 'fn-chat-drawer__bubble-wrap';

      const meta = ownerDocument.createElement('div');
      meta.className = 'fn-chat-drawer__meta';
      const sender = ownerDocument.createElement('span');
      sender.textContent = message.sender;
      const time = ownerDocument.createElement('span');
      time.textContent = message.time;
      meta.append(sender, time);

      const bubble = ownerDocument.createElement('div');
      bubble.className = 'fn-chat-drawer__bubble';
      bubble.textContent = message.content;

      wrap.append(meta, bubble);
      row.append(avatar, wrap);
      fragment.append(row);
    });
    messagesNode.replaceChildren(fragment);
    scrollToBottom();
  }

  function sendMessage() {
    const text = inputNode?.value?.trim() ?? '';
    if (!text) {
      return;
    }

    messages.push({
      id: messageId++,
      sender: USER_NAME,
      content: text,
      time: formatCurrentTime(),
      isMe: true,
      avatarText: '管',
      avatarColor: '#67c23a'
    });
    if (inputNode) {
      inputNode.value = '';
    }
    renderMessages();
  }

  function renderLabels() {
    if (titleNode) {
      titleNode.textContent = translate('shell.chatTitle');
    }
    if (statusNode) {
      statusNode.textContent = isOnline
        ? translate('shell.chatOnline')
        : translate('shell.chatOffline');
    }
    if (inputNode) {
      inputNode.placeholder = translate('shell.chatInputPlaceholder');
      inputNode.setAttribute('aria-label', translate('shell.chatInputPlaceholder'));
    }
    if (sendButton) {
      sendButton.textContent = translate('shell.chatSend');
    }
    if (closeButton) {
      closeButton.setAttribute('aria-label', translate('shell.chatClose'));
    }
    if (openButton) {
      openButton.setAttribute('aria-label', translate('shell.chat'));
    }
  }

  function render(t) {
    if (t) {
      translate = t;
    }

    renderLabels();
    renderMessages();
    if (drawer) {
      drawer.hidden = !isOpen;
      drawer.classList.toggle('is-open', isOpen);
    }
  }

  function open() {
    isOpen = true;
    render();
    inputNode?.focus();
  }

  function close() {
    isOpen = false;
    if (drawer) {
      drawer.hidden = true;
      drawer.classList.remove('is-open');
    }
  }

  function onInputKeydown(event) {
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      sendMessage();
    } else if (event.key === 'Escape') {
      event.preventDefault();
      close();
    }
  }

  const onOpenClick = () => open();
  const onCloseClick = () => close();
  const onBackdropClick = () => close();
  const onSendClick = () => sendMessage();
  const onResize = () => updateViewport();

  openButton?.addEventListener('click', onOpenClick);
  closeButton?.addEventListener('click', onCloseClick);
  backdrop?.addEventListener('click', onBackdropClick);
  sendButton?.addEventListener('click', onSendClick);
  inputNode?.addEventListener('keydown', onInputKeydown);
  window.addEventListener('resize', onResize);
  updateViewport();

  return {
    open,
    close,
    render,
    dispose() {
      openButton?.removeEventListener('click', onOpenClick);
      closeButton?.removeEventListener('click', onCloseClick);
      backdrop?.removeEventListener('click', onBackdropClick);
      sendButton?.removeEventListener('click', onSendClick);
      inputNode?.removeEventListener('keydown', onInputKeydown);
      window.removeEventListener('resize', onResize);
    }
  };
}
