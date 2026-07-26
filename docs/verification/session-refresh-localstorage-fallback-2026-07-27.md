# 浏览器会话刷新跨 Tab 存储回退验证

- 日期：2026-07-27
- 状态：Build-verified
- 范围：`packages/client-contracts` 的无 Web Locks 会话刷新协调

## 缺口

原回退路径把刷新租约写入 `sessionStorage`。该存储按浏览器 Tab 隔离，
所以不同 Tab 会各自认为自己持有锁，无法提供代码声明的跨 Tab 互斥。
`BroadcastChannel` 只传播刷新完成与会话清空事件，不会在刷新开始前阻止
两个 Tab 同时调用服务端 Refresh。

## 修复边界

- 支持 Web Locks 时继续使用浏览器原生锁，不改变主路径。
- 无 Web Locks 时把既有 30 秒、带 owner 的短租约移到同源 Tab 共享的
  `localStorage`。
- 浏览器暴露 `localStorage` 但因隐私或安全策略拒绝读写时，把该异常视为
  存储能力不可用并执行既有无锁降级，不让客户端存储策略阻断会话恢复。
- `localStorage` 中的租约属于非可信持久化输入；JSON 虽可解析但 owner 或
  到期时间结构无效时，清理损坏记录并重新竞争租约，避免空等 30 秒。
- 有效租约的到期时间不得超过“当前时间＋本地 30 秒 TTL”；系统时钟回拨
  或存储污染留下的异常未来租约按损坏记录处理。
- Access Token、Refresh Token、用户资料和权限快照均未写入浏览器存储；
  `localStorage` 只保存随机 Tab owner 与租约到期时间。
- 无 Web Locks 且无 `localStorage` 时继续执行既有无锁降级，不新增服务端
  Refresh Token 重用宽限。

## TDD 证据

RED 使用两个协调器模拟两个 Tab，并让 `sessionStorage` 在读取时抛出
“Tab 私有存储不可用于跨 Tab 锁”的错误。修复前聚焦测试准确失败于
`readStorageLock` 读取 `sessionStorage`。

GREEN 将租约读、写和 owner 校验统一改为 `localStorage`。同一测试中的两个
协调器共享存储，并发刷新期间最大活动操作数为 1；聚焦测试 3/3 通过。

第二轮 RED 让 `localStorage.getItem` 抛出 `SecurityError`，修复前刷新函数
尚未执行便直接失败。GREEN 对存储获取、读、写和清理建立异常边界：写锁
失败时立即降级执行刷新，聚焦测试 4/4 通过。

第三轮 RED 写入可解析但字段类型错误的租约记录；修复前该记录既无法判定
过期，也不会被接管，虚拟时间推进到 30 秒后准确失败于锁超时。GREEN 在
解析边界验证 owner 与有限数值到期时间，损坏记录被清理后刷新立即执行，
聚焦测试 5/5 通过。

第四轮 RED 写入结构正确但到期时间超出本地 TTL 十倍的租约，修复前虚拟
时间推进 30 秒后准确失败于锁超时。GREEN 将本地产生租约的最大时间窗口
纳入输入不变量，异常未来租约被清理后刷新立即执行，聚焦测试 6/6 通过。

## 完整验证

- `pnpm --filter @fullnet/client-contracts test`：78/78。
- `pnpm --filter @fullnet/client-contracts build`：通过。
- `pnpm test:clients`：client-contracts 78/78、Vue 203/203、Layui 95/95、
  admin-i18n 8/8、uni-app 103/103。
- `pnpm test:governance`：11/11。
- `pnpm test:skills`：52 项契约检查通过。
- `pnpm test:workspace`：通过。
- `pnpm audit:clients`：无未复核的 high/critical 风险。
- `git diff --check`：通过。

## 未关闭范围

本切片是确定性单元测试和共享客户端包修复，不代表硬化 Task 9 已完整完成。
仍需真实浏览器多 Context/Page、共享 Cookie、网络故障和服务端 Refresh
重用检测共同参与的端到端故障注入，完成前能力状态保持 `Build-verified`。
