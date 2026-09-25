# 执行清单 80 — OCR 身份证识别 Provider 与受控上传/结果确认页 closeout（2026-09-23）

**范围**：清单 §7.C 编号 **80**（依赖 **Files** Host 文件）；`paddle_ocr_id_card` Provider 配置与连通性测试；`POST /api/v1/ocr/id-card-tasks` 受控识别 + `confirm`/`reject` 人工确认；`OcrIdCardMasking` 列表脱敏；`OcrProviderConfigView` / `OcrIdCardTasksView`；确认结果**不**写入 Identity 权威档案；real-stack **不**调用真实 PaddleOCR 服务。

## 选定切片

| 项 | 值 |
|----|-----|
| Provider | `OcrProviderKeys.PaddleOcrIdCard`；`PaddleOcrIdCardClient` |
| 源文件 | `IHostFileDescriptorReader` / `IHostFileContentReader`；`OcrSourceFileValidator`（jpeg/png/webp，大小上限） |
| 任务状态 | `pending` → `recognized` / `failed` / `provider_unknown` → `confirmed` / `rejected` |
| API | `GET/PUT /api/v1/ocr/provider-configs/{providerKey}`；`POST …/test`；任务 CRUD 式创建 + confirm/reject |
| 凭据 | `OcrApiKeyProtector`；`HasApiKey` 脱敏 |
| Vue | 受控上传（`accept` 图像类型）；确认对话框含「不写入身份权威档案」提示 |
| 范围 | `SqlDataScope.HostOnly`；首切片仅身份证，非通用票据 OCR |

## 交付锚点

| 层 | 位置 |
|----|------|
| 服务 | `OcrIdCardTaskService`；`OcrProviderOperationsService` |
| 解析 | `OcrIdCardResponseParser`；`OcrIdCardPolicy` |
| 单元 | `OcrIdCardMaskingTests`；`OcrIdCardTaskSideEffectTests`；Ocr 过滤器 **11/11** |
| Vitest | `OcrProviderConfigView.test.ts`；`OcrIdCardTasksView.test.ts` |

## 清单 80 验收结论

- **已有**：事务外识别、人工确认路径、Provider 种子行（迁移 199）。
- **本槽**：`phase-c-80-ocr-id-card-tasks.spec.mjs`；`ocr-real-stack.mjs`。
- **未验**：真实 Paddle HTTP 绿路径、上传后端到端识别+确认；uni-app 见 **81** closeout。

## 停止边界

- **81**：uni-app 单平台审批/消息缺项（批次末尾页面验收）。
- 禁止将 OCR 确认结果自动合并到用户/成员权威字段。

## 本机验证

| 命令 | 结果 |
|------|------|
| `dotnet test` …`FullyQualifiedName~Ocr` | **11/11**（`d40de4e6`） |
| `pnpm exec vitest run` `OcrProviderConfigView` + `OcrIdCardTasksView` | **2/2** |
| OpenAPI | `ocrCreateIdCardTask` / `ocrConfirmIdCardTask` / `ocrTestProviderConfig` |
| real-stack | `phase-c-80-ocr-id-card-tasks.spec.mjs`（需 Host + Files） |

**状态**：Build-verified；升 **Verified** 受 Gate0 / real-stack 绿与 Gate C 约束。
