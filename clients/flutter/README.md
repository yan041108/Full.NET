# Full.NET Flutter Client

首个业务切片：**工作流待办审批**（登录 → 待办列表 → 同意/驳回）。

## 平台

- 目标平台：Android、iOS、Windows、macOS、Linux（不含 Web；H5 由 uni-app 负责）
- SDK：Flutter **3.44.0**（见 `.flutter-version`）

首次在本机生成平台工程：

```powershell
flutter create --platforms=android,ios,windows,macos,linux --org net.full --project-name fullnet_client .
```

## 配置

```powershell
flutter run --dart-define=FULLNET_API_BASE_URL=https://localhost:5001 --dart-define=FULLNET_LOCALE=zh-CN
```

访问令牌只保存在内存中，不落本地存储。

## 验证

```powershell
cd clients/flutter
pnpm test
flutter analyze
flutter test
flutter build windows --debug
```

当前仓库 CI 尚未接入 Flutter 构建；`pnpm test` 运行 Node 契约测试，`flutter test` 需在安装 Flutter SDK 后执行。
