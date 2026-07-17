<script setup lang="ts">
import { computed, ref } from 'vue';
import {
  isFullNetProblemDetails,
  type FullNetProblemDetails
} from '@fullnet/client-contracts';
import { useSessionStore } from '../auth/session';

const session = useSessionStore();
const username = ref('');
const password = ref('');
const submitting = ref(false);
const problem = ref<FullNetProblemDetails>();
const status = computed(() => session.state === 'authenticated'
  ? '安全会话已建立'
  : '宿主控制面 · 第一方管理端');

async function submit(): Promise<void> {
  if (submitting.value) {
    return;
  }

  submitting.value = true;
  problem.value = undefined;
  try {
    await session.login(username.value, password.value);
  } catch (error: unknown) {
    problem.value = isFullNetProblemDetails(error)
      ? error
      : { status: 500, code: 'client.login_failed', title: '登录请求未完成' };
  } finally {
    submitting.value = false;
  }
}
</script>

<template>
  <main class="login-gate" data-testid="login-view">
    <section class="login-signal" aria-label="Full.NET 平台说明">
      <div class="login-brand"><span>F</span><div><strong>Full.NET</strong><small>CONTROL PLANE</small></div></div>
      <div class="login-statement">
        <p>IDENTITY / SESSION NODE</p>
        <h1>进入你的<br><em>交付控制面</em></h1>
        <span>短期访问令牌只驻留内存；刷新会话由安全 Cookie、CSRF 与重用检测共同保护。</span>
      </div>
      <div class="login-telemetry">
        <div><b>10m</b><span>ACCESS WINDOW</span></div>
        <div><b>RSA</b><span>SIGNING RING</span></div>
        <div><b>2×DB</b><span>PROVIDER PARITY</span></div>
      </div>
    </section>

    <section class="login-panel">
      <form @submit.prevent="submit">
        <div class="login-panel__head">
          <span>01 / AUTHENTICATE</span>
          <i aria-hidden="true" />
        </div>
        <h2>管理员登录</h2>
        <p>{{ status }}</p>

        <label>
          <span>账号</span>
          <input v-model.trim="username" name="username" autocomplete="username" maxlength="128" required placeholder="输入宿主管理员账号">
        </label>
        <label>
          <span>密码</span>
          <input v-model="password" name="password" type="password" autocomplete="current-password" maxlength="1024" required placeholder="输入安全密码">
        </label>

        <div v-if="problem" class="login-problem" role="alert">
          <strong>{{ problem.code }}</strong>
          <span>{{ problem.title }}</span>
          <code v-if="problem.traceId">{{ problem.traceId }}</code>
        </div>

        <button type="submit" :disabled="submitting">
          <span>{{ submitting ? '正在建立会话' : '进入控制台' }}</span><b>→</b>
        </button>
        <small class="login-footnote">系统不会在浏览器持久化 Access Token</small>
      </form>
    </section>
  </main>
</template>

<style scoped>
.login-gate { display: grid; min-height: 100vh; grid-template-columns: minmax(420px, 1.25fr) minmax(410px, .75fr); background: #f4f3ec; color: #172027; }
.login-signal { position: relative; display: flex; min-height: 100vh; flex-direction: column; overflow: hidden; padding: clamp(28px, 4vw, 64px); background: #172027; color: #fff; }
.login-signal::before { position: absolute; inset: 0; background: linear-gradient(115deg, transparent 45%, rgb(66 185 166 / 8%)), repeating-linear-gradient(90deg, transparent 0 79px, rgb(255 255 255 / 3%) 80px); content: ""; }
.login-signal::after { position: absolute; right: -16vw; bottom: -22vw; width: 56vw; height: 56vw; border: 1px solid rgb(66 185 166 / 25%); border-radius: 50%; box-shadow: 0 0 0 7vw rgb(66 185 166 / 3%), 0 0 0 14vw rgb(66 185 166 / 2%); content: ""; }
.login-brand, .login-statement, .login-telemetry { position: relative; z-index: 1; }
.login-brand { display: flex; align-items: center; gap: 13px; }
.login-brand > span { display: grid; width: 38px; height: 38px; place-items: center; background: #42b9a6; color: #172027; font-family: var(--fullnet-font-display); font-weight: 800; }
.login-brand strong, .login-brand small { display: block; }
.login-brand strong { font-family: var(--fullnet-font-display); font-size: 17px; }
.login-brand small { margin-top: 3px; color: #73858a; font-size: 7px; letter-spacing: .2em; }
.login-statement { max-width: 680px; margin: auto 0; }
.login-statement p { color: #42b9a6; font-family: var(--fullnet-font-display); font-size: 10px; font-weight: 700; letter-spacing: .22em; }
.login-statement h1 { margin: 20px 0; font-family: var(--fullnet-font-display); font-size: clamp(50px, 6.2vw, 92px); font-weight: 430; letter-spacing: -.065em; line-height: .94; }
.login-statement h1 em { color: #d99b35; font-style: normal; }
.login-statement > span { display: block; max-width: 570px; color: #9ba8aa; font-size: 14px; line-height: 1.9; }
.login-telemetry { display: grid; grid-template-columns: repeat(3, 1fr); border-top: 1px solid rgb(255 255 255 / 10%); }
.login-telemetry div { padding: 22px 0; border-right: 1px solid rgb(255 255 255 / 8%); }
.login-telemetry b, .login-telemetry span { display: block; }
.login-telemetry b { font-family: var(--fullnet-font-display); font-size: 24px; font-weight: 520; }
.login-telemetry span { margin-top: 6px; color: #708085; font-size: 8px; letter-spacing: .14em; }
.login-panel { display: grid; place-items: center; padding: clamp(28px, 5vw, 80px); }
form { width: min(100%, 430px); }
.login-panel__head { display: flex; align-items: center; justify-content: space-between; color: #0b8f87; font-family: var(--fullnet-font-display); font-size: 9px; font-weight: 700; letter-spacing: .18em; }
.login-panel__head i { width: 50px; height: 1px; background: #0b8f87; }
h2 { margin: 30px 0 9px; font-family: var(--fullnet-font-display); font-size: 38px; font-weight: 520; letter-spacing: -.045em; }
form > p { margin: 0 0 38px; color: #7c8886; font-size: 12px; }
label { display: block; margin: 0 0 22px; }
label span { display: block; margin-bottom: 9px; color: #53605f; font-size: 11px; font-weight: 700; }
input { width: 100%; height: 52px; box-sizing: border-box; padding: 0 15px; border: 1px solid #d4d9d3; border-radius: 2px; outline: none; background: #fffef9; color: #172027; font: inherit; transition: border-color .18s, box-shadow .18s; }
input:focus { border-color: #0b8f87; box-shadow: 0 0 0 3px rgb(11 143 135 / 10%); }
.login-problem { display: grid; gap: 4px; margin: 4px 0 18px; padding: 12px 14px; border-left: 3px solid #c94a4a; background: rgb(201 74 74 / 7%); font-size: 11px; }
.login-problem strong { color: #c94a4a; }
.login-problem code { overflow: hidden; color: #7d8685; text-overflow: ellipsis; }
form > button { display: flex; width: 100%; height: 54px; align-items: center; justify-content: space-between; margin-top: 28px; padding: 0 20px; border: 0; background: #172027; color: #fff; font: inherit; font-weight: 700; cursor: pointer; transition: background .18s, transform .18s; }
form > button:hover:not(:disabled) { background: #0b8f87; transform: translateY(-2px); }
form > button:disabled { cursor: wait; opacity: .65; }
form > button b { color: #42b9a6; font-size: 20px; }
.login-footnote { display: block; margin-top: 14px; color: #8a9491; text-align: center; }
@media (max-width: 900px) { .login-gate { grid-template-columns: 1fr; } .login-signal { min-height: 340px; } .login-statement { margin: 70px 0; } .login-statement h1 { font-size: 54px; } .login-telemetry { display: none; } .login-panel { min-height: 620px; } }
</style>
