# Full.NET Full-Stack Localization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** 让 ASP.NET Core、Vue、Layui、uni-app 和 Flutter 使用统一语言契约，并以各平台原生资源机制完整支持 zh-CN/en-US。

**Architecture:** 仓库级清单统一 BCP 47 标签、回退、术语和错误语义；服务端、Web、小程序和 Flutter 分别编译原生资源。HTTP 使用 Accept-Language/Content-Language，业务逻辑只依赖稳定 code，账号与租户保存规范语言偏好。

**Tech Stack:** ASP.NET Core 10 localization、IStringLocalizer/.resx、Dapper、DbUp、System.Text.Json、Vue 3、Element Plus、Layui 2.13.8 i18n、uni-app Vue 3/Vue I18n、Flutter gen_l10n/ARB、Vitest、Playwright、Microsoft.Testing.Platform、Testcontainers。

## Global Constraints

- 首期生产语言固定为 zh-CN 与 en-US，默认 zh-CN；新增语言必须通过完整资源和平台构建门禁。
- 对外语言标签固定使用 BCP 47；uni-app 的 zh-Hans 和 Flutter 的 zh_CN 只存在于平台适配层。
- status、code、traceId、权限码、字段路径、枚举、路由语义和 Tool Schema 禁止本地化。
- JSON 仍使用 System.Text.Json；外部错误仍为标准 HTTP + ProblemDetails；Admin.NET 包络只在兼容层。
- 数据库变更必须同时提供 SQL Server/MySQL 迁移和真实集成测试。
- 本地化缓存 key 必须包含规范 locale；系统字符串编译进资源，不在请求热路径读取磁盘或远程字典。
- Vue 与 Layui 的管理功能按同一场景同步验收；uni-app 三目标和 Flutter 声明平台分别构建。
- 所有新依赖必须锁定版本、审计许可证和发布物边界；不得复制 layuiAdmin 产品资产。
- 每个行为任务都执行 RED → GREEN → REFACTOR，并单独提交可审查结果。

## File Structure

实施后的关键结构：

~~~text
localization/
├── locales.json
├── glossary.json
├── README.md
└── schemas/locale-catalog.schema.json

src/BuildingBlocks/Full.NET.Localization/
├── Full.NET.Localization.csproj
├── FullNetLocalizationOptions.cs
├── LocaleCatalog.cs
├── LocaleNormalizer.cs
├── LocaleContext.cs
├── CultureScope.cs
├── LocalizationServiceCollectionExtensions.cs
└── LocalizationApplicationBuilderExtensions.cs

src/BuildingBlocks/Full.NET.Hosting/Resources/
├── CommonErrors.resx
└── CommonErrors.en-US.resx

clients/uniapp/src/i18n/
├── index.ts
├── locale-adapter.ts
├── messages.zh-CN.json
└── messages.en-US.json

clients/flutter/
├── l10n.yaml
└── lib/l10n/
    ├── app_en.arb
    └── app_zh_CN.arb
~~~

---

### Task 1: 建立仓库级语言清单和自动门禁

**Files:**
- Create: localization/locales.json
- Create: localization/glossary.json
- Create: localization/schemas/locale-catalog.schema.json
- Create: localization/README.md
- Create: tests/localization-contract.test.mjs
- Modify: package.json
- Modify: tests/client-workspace.test.mjs

**Interfaces:**
- Produces: defaultLocale、supportedLocales、platformMappings、fallbacks、direction 和术语清单。
- Consumes: 当前 @fullnet/admin-i18n 的 zh-CN/en-US 列表。

- [ ] **Step 1: 先写会失败的清单契约测试**

测试读取 localization/locales.json，断言默认语言存在于 supportedLocales、tag 唯一、platformMappings 完整、回退无环，并读取 packages/admin-i18n/src/locale.ts 验证两者包含相同规范语言。

~~~javascript
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

test('locale catalog defines two canonical production locales', async () => {
  const catalog = JSON.parse(
    await readFile(new URL('../localization/locales.json', import.meta.url), 'utf8')
  );
  assert.equal(catalog.defaultLocale, 'zh-CN');
  assert.deepEqual(
    catalog.supportedLocales.map(item => item.tag),
    ['zh-CN', 'en-US']
  );
  for (const item of catalog.supportedLocales) {
    assert.ok(item.platformMappings.dotnet);
    assert.ok(item.platformMappings.web);
    assert.ok(item.platformMappings.uniapp);
    assert.ok(item.platformMappings.flutter);
  }
});
~~~

- [ ] **Step 2: 运行 RED**

Run: node --test tests/localization-contract.test.mjs
Expected: FAIL，localization/locales.json 不存在。

- [ ] **Step 3: 写入最小清单**

locales.json 的固定数据：

~~~json
{
  "schemaVersion": 1,
  "defaultLocale": "zh-CN",
  "supportedLocales": [
    {
      "tag": "zh-CN",
      "fallbacks": ["zh", "zh-Hans", "zh-SG"],
      "direction": "ltr",
      "platformMappings": {
        "dotnet": "zh-CN",
        "web": "zh-CN",
        "uniapp": "zh-Hans",
        "flutter": "zh_CN"
      }
    },
    {
      "tag": "en-US",
      "fallbacks": ["en", "en-GB"],
      "direction": "ltr",
      "platformMappings": {
        "dotnet": "en-US",
        "web": "en-US",
        "uniapp": "en",
        "flutter": "en_US"
      }
    }
  ]
}
~~~

glossary.json 至少固定 Full.NET、Host、Tenant、TraceId、Access Token、Refresh Token、ProblemDetails、SignalR、Agent、Tool 与 MCP 的中英文显示和 translate=false 规则。

- [ ] **Step 4: 把门禁接入工作区验证**

package.json 增加：

~~~json
"test:localization": "node --test tests/localization-contract.test.mjs"
~~~

tests/client-workspace.test.mjs 增加 localization 目录、脚本和清单字段断言。

- [ ] **Step 5: 运行 GREEN**

Run: pnpm test:localization && pnpm test:workspace
Expected: 两个命令均退出 0。

- [ ] **Step 6: 提交**

~~~powershell
git add localization package.json tests/localization-contract.test.mjs tests/client-workspace.test.mjs
git commit -m "feat: add localization governance contract"
~~~

### Task 2: 建立 ASP.NET Core 本地化 BuildingBlock

**Files:**
- Create: src/BuildingBlocks/Full.NET.Localization/Full.NET.Localization.csproj
- Create: src/BuildingBlocks/Full.NET.Localization/FullNetLocalizationOptions.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocaleCatalog.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocaleNormalizer.cs
- Create: src/BuildingBlocks/Full.NET.Localization/ILocaleContext.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocaleContext.cs
- Create: src/BuildingBlocks/Full.NET.Localization/CultureScope.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocalizationHttpHeaders.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocalizationServiceCollectionExtensions.cs
- Create: src/BuildingBlocks/Full.NET.Localization/LocalizationApplicationBuilderExtensions.cs
- Modify: Full.NET.slnx
- Modify: src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj
- Modify: src/BuildingBlocks/Full.NET.Hosting/Observability/ServiceDefaultsExtensions.cs
- Modify: src/Hosts/Full.NET.Host.Api/Program.cs
- Create: tests/Full.NET.UnitTests/Localization/LocaleNormalizerTests.cs
- Create: tests/Full.NET.UnitTests/Localization/CultureScopeTests.cs
- Create: tests/Full.NET.UnitTests/Localization/LocalizationHttpHeadersTests.cs

**Interfaces:**
- Produces: ILocaleContext.CurrentLocale、ILocaleNormalizer.Normalize、AddFullNetLocalization、UseFullNetLocalization、CultureScope.Push。
- Consumes: Task 1 的规范语言清单；生产运行时使用编译后的固定 options，不在请求中读取 JSON。

- [ ] **Step 1: 写 LocaleNormalizer 与 CultureScope 的失败测试**

覆盖 zh-Hans → zh-CN、en-GB → en-US、非法值 → zh-CN，以及并行 CultureScope 离开后恢复调用方 CurrentCulture/CurrentUICulture。

- [ ] **Step 2: 运行 RED**

Run: dotnet build Full.NET.slnx --configuration Release
Expected: FAIL，Full.NET.Localization 类型不存在。

- [ ] **Step 3: 定义公开接口**

~~~csharp
public interface ILocaleNormalizer
{
    string DefaultLocale { get; }
    IReadOnlyList<string> SupportedLocales { get; }
    string Normalize(string? requestedLocale);
    bool IsSupported(string? locale);
}

public interface ILocaleContext
{
    string CurrentLocale { get; }
}
~~~

FullNetLocalizationOptions 默认 SupportedLocales 为 zh-CN/en-US，DefaultLocale 为 zh-CN。Options validator 必须拒绝空列表、重复标签、默认语言缺失和无效 CultureInfo。

- [ ] **Step 4: 配置请求文化**

AddFullNetLocalization 注册 AddLocalization、Options validator、LocaleNormalizer 和 LocaleContext。RequestLocalizationOptions 只保留 AcceptLanguageHeaderRequestCultureProvider，设置 SupportedCultures、SupportedUICultures 与 DefaultRequestCulture。

Full.NET.Localization 与 Hosting 的项目文件将 NeutralLanguage 固定为 zh-CN，保证中性 .resx 是可预测的中文回退，而不是依赖构建机器文化。

UseFullNetLocalization 只负责在管道早期调用 UseRequestLocalization。`LocalizationHttpHeaders.Apply(HttpResponse, locale, varyByAcceptLanguage)` 为确定产生本地化文本的边界设置 Content-Language，并在 `varyByAcceptLanguage=true` 时无重复追加 Vary: Accept-Language；Task 3 的 ProblemDetails Mapper 使用该帮助器。语言中立成功 DTO 不添加 Vary，避免破坏公共缓存命中率。

- [ ] **Step 5: 调整中间件顺序**

Program.cs 固定为：

~~~csharp
app.UseFullNetLocalization();
app.UseFullNetRequestLogging();
app.UseExceptionHandler();
app.UseCors(IdentityModule.BrowserCorsPolicy);
~~~

本地化必须早于异常处理、认证、租户和授权。

- [ ] **Step 6: 运行 GREEN**

Run: dotnet build Full.NET.slnx --configuration Release --no-restore

Run: dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 161 --timeout 5m
Expected: 构建 0 错误，本任务新增测试与原 116 项共 144 项全部通过。

- [ ] **Step 7: 提交**

~~~powershell
git add Full.NET.slnx src/BuildingBlocks/Full.NET.Localization src/BuildingBlocks/Full.NET.Hosting src/Hosts/Full.NET.Host.Api tests/Full.NET.UnitTests/Localization
git commit -m "feat: add fullnet request localization"
~~~

### Task 3: 本地化 ProblemDetails 并增加结构化验证违反项

**Files:**
- Modify: src/BuildingBlocks/Full.NET.Abstractions/Results/Error.cs
- Create: src/BuildingBlocks/Full.NET.Abstractions/Results/CommonErrorCodes.cs
- Create: src/BuildingBlocks/Full.NET.Abstractions/Results/ValidationErrorCodes.cs
- Create: src/BuildingBlocks/Full.NET.Abstractions/Results/ValidationViolation.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Api/IErrorMessageLocalizer.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Api/IErrorResourceSource.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Api/ResourceManagerErrorResourceSource.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Api/NamedMessageFormatter.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Api/ResourceErrorMessageLocalizer.cs
- Create: src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.resx
- Create: src/BuildingBlocks/Full.NET.Hosting/Resources/CommonErrors.en-US.resx
- Create: src/BuildingBlocks/Full.NET.Hosting/Serialization/HostingJsonSerializerContext.cs
- Modify: src/BuildingBlocks/Full.NET.Hosting/Api/StandardApiResultMapper.cs
- Modify: src/Compatibility/Full.NET.Compatibility.AdminNet/AdminNetApiResultMapper.cs
- Modify: src/BuildingBlocks/Full.NET.Validation.FluentValidation/FluentValidationBehavior.cs
- Create: src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.resx
- Create: src/Modules/Full.NET.Modules.Identity/Resources/IdentityErrors.en-US.resx
- Create: src/Modules/Full.NET.Modules.Identity/Contracts/IdentityErrorCodes.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Full.NET.Modules.Identity.csproj
- Create: src/Modules/Full.NET.Modules.Tenancy/Resources/TenancyErrors.resx
- Create: src/Modules/Full.NET.Modules.Tenancy/Resources/TenancyErrors.en-US.resx
- Create: src/Modules/Full.NET.Modules.Tenancy/Contracts/TenancyErrorCodes.cs
- Modify: src/Modules/Full.NET.Modules.Tenancy/Full.NET.Modules.Tenancy.csproj
- Modify: tests/Full.NET.UnitTests/Hosting/StandardApiResultMapperTests.cs
- Create: tests/Full.NET.UnitTests/Localization/ErrorResourceCompletenessTests.cs
- Create: tests/Full.NET.IntegrationTests/Api/LocalizedProblemDetailsTests.cs
- Modify: tests/Full.NET.CompatibilityTests/AdminNetApiResultMapperTests.cs
- Modify: src/BuildingBlocks/Full.NET.Hosting/Serialization/FullNetJsonOptionsExtensions.cs

**Interfaces:**
- Produces: `IErrorResourceSource(Prefix, TryGetTemplate)`、`IErrorMessageLocalizer.Localize(Error, CultureInfo)`；ProblemDetails extensions violations 的元素为 field、code、arguments；title/detail/errors 为本地化展示字段。
- Consumes: Error.Code、Error.Type、Error.Arguments、Error.ValidationViolations、Task 2 的 LocalizationHttpHeaders 与请求 CurrentUICulture。

- [ ] **Step 1: 写失败的双语言 API 测试**

同一错误分别发送 Accept-Language: zh-CN 与 en-US，断言 status/code/traceId/violations 相同、title 不同、Content-Language 正确；未知语言回退 zh-CN。

- [ ] **Step 2: 写失败的资源完整性测试**

从 Common、Validation、Identity、Tenancy 的稳定 ErrorCodes 常量目录枚举 code，要求对应 `IErrorResourceSource` 的默认资源和 en-US 资源均有条目。实现时把现有 Error 构造中的字符串 code 收敛到所属目录，禁止通过扫描 IL 或正则猜测运行时错误码。

- [ ] **Step 3: 扩展错误模型**

~~~csharp
public sealed record ValidationViolation(
    string Field,
    string Code,
    IReadOnlyDictionary<string, object?> Arguments);

public sealed record Error
{
    public Error(
        string Code,
        string Message,
        ErrorType Type,
        IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

    public Error(
        string Code,
        string Message,
        ErrorType Type,
        IReadOnlyDictionary<string, string[]>? ValidationErrors,
        IReadOnlyDictionary<string, object?>? Arguments,
        IReadOnlyList<ValidationViolation>? ValidationViolations);

    public string Message { get; init; }

    [JsonIgnore]
    public string DefaultMessage => Message;
}
~~~

必须保留旧四参数构造、`Message` init 属性与四元 `Deconstruct`；扩展构造把新参数置于旧四参数之后，禁止让三参数或第四参数 `null` 的调用产生歧义。`DefaultMessage` 只作为不参与 JSON 的安全语义别名；序列化必须继续输出 `message`，新增 `arguments/validationViolations` 只能是 additive。

- [ ] **Step 4: 实现资源聚合与映射**

ResourceErrorMessageLocalizer 按最长 code 前缀选择模块注册的 `IErrorResourceSource`，资源前缀必须以 `.` 结束，按 CurrentUICulture 使用 `NamedMessageFormatter` 格式化命名参数。格式器只替换资源中与 Arguments 精确匹配的 `{Name}`，不解释 HTML、表达式或任意格式代码。缺失资源或参数时返回 DefaultMessage，并使用低基数指标记录 code/locale；不得记录用户参数。稳定 Meter 名称必须由常量统一提供，并通过 ServiceDefaults `.AddMeter(...)` 接入 OpenTelemetry。

StandardApiResultMapper 保留 code、traceId、status、type 和兼容 errors，同时增加 violations，并通过 Task 2 帮助器设置 Content-Language 与 Vary。AdminNetApiResultMapper 注入同一个 IErrorMessageLocalizer，只改变外层包络而不建立第二份资源。为新增 DTO 建立 Hosting JsonSerializerContext 并插入 TypeInfoResolverChain。

Identity 与 Tenancy 项目文件将 NeutralLanguage 固定为 zh-CN，并以 `TryAddEnumerable` 注册模块自己的 `IErrorResourceSource`；source 在模块内通过 ResourceManager 基名读取资源。Hosting 只依赖来源接口，不引用模块类型或程序集。

- [ ] **Step 5: 让 FluentValidation 产生稳定 code**

每条规则设置 ErrorCode，ValidationFailure.PropertyName 作为 Field，PlaceholderValues 中受允许的长度/范围参数写入 Arguments。Identity 密码策略的长度、大写、小写、数字和非字母数字要求也必须分别产生稳定 code，且最小长度只公开 `MinLength`。ValidationErrors 继续生成同序文本以兼容已有客户端，必须与 violations 一一对应；映射器面对旧生产者数量失配时保留未配对消息，禁止静默截断或记录可能含用户输入的文本。

- [ ] **Step 6: 运行验证**

Run: dotnet build Full.NET.slnx --configuration Release --no-restore

Run: dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 161 --timeout 5m

Run: dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 10 --timeout 10m
Expected: 单元和 SQL Server/MySQL 集成全部通过；两种语言仅展示文本不同。

- [ ] **Step 7: 提交**

~~~powershell
git add src tests/Full.NET.UnitTests/Hosting tests/Full.NET.UnitTests/Localization tests/Full.NET.IntegrationTests/Api
git commit -m "feat: localize structured problem details"
~~~

### Task 4: 保存账号语言偏好与租户默认语言

**Files:**
- Create: src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/SqlServer/004_LocalizationPreferences.sql
- Create: src/BuildingBlocks/Full.NET.Migrations.DbUp/Migrations/MySql/004_LocalizationPreferences.sql
- Modify: src/Modules/Full.NET.Modules.Identity/Domain/IdentityUser.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Persistence/IdentityUserRecord.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Persistence/RefreshSessionRecord.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Persistence/IdentitySql.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Contracts/CurrentUserResponse.cs
- Create: src/Modules/Full.NET.Modules.Identity/Contracts/UpdateLocaleRequest.cs
- Create: src/Modules/Full.NET.Modules.Identity/Contracts/LocalePreferenceResponse.cs
- Modify: src/Modules/Full.NET.Modules.Identity/Features/GetCurrentUser/Endpoint.cs
- Create: src/Modules/Full.NET.Modules.Identity/Features/UpdateLocale/Command.cs
- Create: src/Modules/Full.NET.Modules.Identity/Features/UpdateLocale/Handler.cs
- Create: src/Modules/Full.NET.Modules.Identity/Features/UpdateLocale/Endpoint.cs
- Create: src/Modules/Full.NET.Modules.Identity/Features/UpdateLocale/Validator.cs
- Modify: src/Modules/Full.NET.Modules.Identity/IdentityModule.cs
- Modify: src/Modules/Full.NET.Modules.Tenancy/Domain/Tenant.cs
- Modify: src/Modules/Full.NET.Modules.Tenancy/Contracts/TenantSummary.cs
- Modify: src/Modules/Full.NET.Modules.Tenancy/Persistence/TenantSql.cs
- Modify: src/BuildingBlocks/Full.NET.Migrations.DbUp/DbUpMigrationRunner.cs
- Modify: tests/Full.NET.IntegrationTests/Migrations/SqlServerMigrationTests.cs
- Modify: tests/Full.NET.IntegrationTests/Migrations/MySqlMigrationTests.cs
- Create: tests/Full.NET.UnitTests/Identity/UpdateLocaleHandlerTests.cs
- Modify: tests/Full.NET.IntegrationTests/Tenancy/TenantProvisioningTests.cs
- Create: tests/Full.NET.IntegrationTests/Identity/LocalePreferenceTests.cs

**Interfaces:**
- Produces: CurrentUserResponse.PreferredLocale/ProfileVersion、PUT /api/v1/me/locale、Tenant.DefaultLocale。
- Consumes: ILocaleNormalizer；用户 ID 和租户只来自认证上下文。

- [x] **Step 1: 使用 fullnet-module-delivery Skill 建立 RED 双库测试**

测试 SQL Server/MySQL：默认 zh-CN/ProfileVersion=1；用户更新 en-US 后 /api/v1/me 返回 en-US 与新 ProfileVersion；非法值返回 400/localization.unsupported_locale；旧 ProfileVersion 并发更新返回 409/identity.profile_version_conflict；租户默认语言不能覆盖用户偏好。测试必须从真实登录取得令牌，且请求体不得出现 UserId、TenantId 或 ScopeKey。

- [x] **Step 2: 运行 RED**

Run: dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 10 --timeout 10m
Expected: FAIL，迁移字段和 Endpoint 不存在。

- [x] **Step 3: 增加双库迁移**

Identity 用户增加 PreferredLocale（varchar/nvarchar(35)、NOT NULL、默认 zh-CN）和独立 ProfileVersion（int、NOT NULL、默认 1），Tenancy 增加 DefaultLocale（varchar/nvarchar(35)、NOT NULL、默认 zh-CN）；迁移先回填再收紧约束，SQL Server/MySQL 使用各自合法语法。非事务或隐式提交 DDL 必须允许在 DbUp 未记账、结构部分完成后重跑收敛，并以两库真实恢复测试覆盖。ProfileVersion 只保护展示资料更新，不得复用或推进参与登录、锁定、SecurityStamp 与 Refresh Session 校验的 IdentityUser.Version。

- [x] **Step 4: 实现 Dapper 命令**

UpdateLocale Handler 从认证主体的签名 sub 与 ActorScope 取得用户边界，以 ProfileVersion 做乐观并发更新并只持久化规范语言标签。请求体只含 locale/profileVersion；禁止客户端提交 UserId、TenantId 或 ScopeKey。Validator 负责空值与版本形状；非空但不受支持的语言由 Handler 返回顶层 localization.unsupported_locale，避免被通用 validation.failed 包络掩盖。更新成功返回规范 PreferredLocale 与递增后的 ProfileVersion；0 行更新必须用同一签名 UserId + ScopeKey 重读，账号缺失或停用返回 401，账号仍活动才返回 409 并发冲突。

- [x] **Step 5: 更新会话 DTO**

/api/v1/me 必须依据签名 sub 与 ActorScope 查询 Identity 数据库记录，返回当前 PreferredLocale 与 ProfileVersion；不能从 JWT 或请求 Header 推断已保存偏好。登录、刷新和租户切换后的既有 hydrate 继续读取该 Endpoint。PreferredLocale/ProfileVersion 不写入 Access Token Claim，避免展示偏好触发令牌轮换或进入授权语义；读取与更新 SQL 使用 Global scope 仅因为身份边界来自已验证 Claim，并必须同时限定 UserId 与 ActorScope。

- [x] **Step 6: 运行 GREEN**

Run: dotnet build Full.NET.slnx --configuration Release --no-restore

Run: dotnet tests/Full.NET.UnitTests/bin/Release/net10.0/Full.NET.UnitTests.dll --no-ansi --progress off --minimum-expected-tests 171 --timeout 5m

Run: dotnet tests/Full.NET.IntegrationTests/bin/Release/net10.0/Full.NET.IntegrationTests.dll --no-ansi --progress off --minimum-expected-tests 18 --timeout 15m
Expected: SQL Server/MySQL 全部通过。

- [x] **Step 7: 提交**

~~~powershell
git add src/BuildingBlocks/Full.NET.Migrations.DbUp src/Modules/Full.NET.Modules.Identity src/Modules/Full.NET.Modules.Tenancy tests/Full.NET.IntegrationTests
git commit -m "feat: persist locale preferences"
~~~

### Task 5: 补齐 Vue/Layui 组件库语言与 HTTP 协商

**Files:**
- Modify: packages/client-contracts/src/identity.ts
- Modify: packages/client-contracts/src/index.ts
- Modify: packages/client-contracts/tests/identity.test.ts
- Modify: packages/admin-i18n/src/locale.ts
- Modify: packages/admin-i18n/src/messages.ts
- Modify: packages/admin-i18n/tests/i18n.test.ts
- Modify: ui/admin/src/api/http.ts
- Modify: ui/admin/src/api/http.test.ts
- Modify: ui/admin/src/App.vue
- Create: ui/admin/src/i18n/elementLocale.ts
- Create: ui/admin/src/i18n/elementLocale.test.ts
- Modify: ui/admin/src/auth/session.ts
- Modify: ui/admin/src/auth/session.test.ts
- Modify: ui/admin-layui/js/core/http.js
- Modify: ui/admin-layui/js/core/i18n.js
- Modify: ui/admin-layui/js/main.js
- Create: ui/admin-layui/js/core/layui-locale.js
- Create: ui/admin-layui/tests/layui-locale.test.js
- Modify: tests/e2e/admin-parity/tests/accessibility-i18n.spec.mjs

**Interfaces:**
- Produces: 所有 HTTP 请求的 Accept-Language、Element Plus/Day.js locale、Layui 公开 i18n 配置、账号偏好同步。
- Consumes: Task 1 语言清单、Task 4 PreferredLocale/PUT Endpoint。

- [x] **Step 1: 写两端失败测试**

客户端契约先断言 `/api/v1/me` 必须包含规范 `preferredLocale` 与正整数 `profileVersion`，并为 `PUT /api/v1/me/locale` 响应提供独立守卫。Vue 断言 ElConfigProvider locale 与 adminI18n.locale 同步；Layui 断言公开 `i18n.set` 在 table/laypage/laydate 第一次渲染前执行；两端 HTTP 测试断言 Accept-Language。

- [x] **Step 2: 运行 RED**

Run: pnpm test:clients
Expected: FAIL，组件库 locale 与 Header 尚未接入。

- [x] **Step 3: 实现 Vue 组件语言**

elementLocale.ts 精确映射：

~~~typescript
export const elementLocaleLoaders = {
  'zh-CN': () => import('element-plus/es/locale/lang/zh-cn'),
  'en-US': () => import('element-plus/es/locale/lang/en')
} as const;
~~~

App.vue 以 ElConfigProvider 包裹应用；切换时同步 Day.js zh-cn/en。加载失败回退 zh-CN 并保留稳定页面。

- [x] **Step 4: 实现 Layui 组件语言**

layui-locale.js 只调用公开 layui.i18n.set，传入 zh-CN/en 组件消息；初始化发生在任何 table、laydate、layer、form、upload 渲染前。禁止调用 i18n.$t 私有方法。

- [x] **Step 5: 同步 HTTP 与账号偏好**

两端 request 在每次发送前读取当前活动语言并设置 Accept-Language。Access Token 与登录/刷新 TokenResponse 不携带语言偏好；登录、刷新、恢复和租户切换后的 `/api/v1/me` 是已保存 `PreferredLocale/ProfileVersion` 的唯一来源，并在完整快照通过守卫后同步 i18n。匿名切换只更新本地偏好；已认证切换使用当前 ProfileVersion 调用 PUT `/api/v1/me/locale`，只在响应通过守卫后提交本地语言与版本。保存失败显示本地化提示，且不得清除会话、改变租户或乐观覆盖原语言。

- [x] **Step 6: 扩展双端 E2E**

同场景断言：Element Plus/Layui 分页与日期组件切换；请求 Header 为 en-US；刷新恢复英文；保存失败不会退出或改变租户；切换语言后 403/ProblemDetails 的 status/code 不变。

- [x] **Step 7: 运行 GREEN**

Run: pnpm test:workspace

Run: pnpm test:clients

Run: pnpm build:clients

Run: pnpm test:e2e
Expected: 工作区、全部客户端单元、两套生产构建与双端 E2E 全部通过。

- [x] **Step 8: 提交**

~~~powershell
git add packages/client-contracts packages/admin-i18n ui/admin ui/admin-layui tests/e2e/admin-parity
git commit -m "feat: complete dual admin localization"
~~~

### Task 6: 建立 uni-app 三目标多语言基础

详细实施与验收边界见 [`2026-07-18-uniapp-localization-foundation.md`](2026-07-18-uniapp-localization-foundation.md)。该子计划固定当前官方稳定依赖、登录用户原子切换语义、三目标构建门禁与“缺少平台开发者工具时不得标记真机已验证”的状态规则。

**Files:**
- Create: clients/uniapp/package.json
- Create: clients/uniapp/tsconfig.json
- Create: clients/uniapp/vite.config.ts
- Create: clients/uniapp/src/main.ts
- Create: clients/uniapp/src/App.vue
- Create: clients/uniapp/src/pages.json
- Create: clients/uniapp/src/manifest.json
- Create: clients/uniapp/src/locale/uni-app.zh-Hans.json
- Create: clients/uniapp/src/locale/uni-app.en.json
- Create: clients/uniapp/src/i18n/index.ts
- Create: clients/uniapp/src/i18n/locale-adapter.ts
- Create: clients/uniapp/src/i18n/messages.zh-CN.json
- Create: clients/uniapp/src/i18n/messages.en-US.json
- Create: clients/uniapp/src/api/http.ts
- Create: clients/uniapp/src/pages/settings/locale.vue
- Create: clients/uniapp/tests/locale-adapter.test.ts
- Create: clients/uniapp/tests/http-locale.test.ts
- Modify: package.json
- Modify: pnpm-lock.yaml

**Interfaces:**
- Produces: resolveUniLocale、toCanonicalLocale、setActiveLocale、uni.request Accept-Language。
- Consumes: Task 1 规范语言，Task 4 账号偏好，标准 ProblemDetails。

- [x] **Step 1: 固定工具链版本**

从 uni-app 官方 CLI/Vite Vue 3 TypeScript 模板创建工程；@dcloudio 运行时与构建包统一固定为 `3.0.0-5010520260709002`，Vue/Vue compiler 固定为 `3.4.21`，`@dcloudio/types` 固定为 `3.4.31`。安全审计后将 Vue I18n 固定为 `9.14.5`、Vitest 固定为 `3.2.6`、Vite 固定为经三端构建和 H5 E2E 验证的 `5.4.21`；DCloud 插件仍声明精确 peer `5.2.8`，该偏差必须通过版本化、路径受限且可过期的安全策略管理，不能宣称获得上游正式支持。TypeScript 与 vue-tsc 同样使用精确版本，禁止 latest、星号或未锁定插件市场依赖。记录 Node 24/pnpm 10.26.0 的实际兼容验证。

- [x] **Step 2: 写 RED 适配测试**

~~~typescript
expect(toCanonicalLocale('zh-Hans')).toBe('zh-CN');
expect(toCanonicalLocale('en')).toBe('en-US');
expect(toUniLocale('zh-CN')).toBe('zh-Hans');
expect(toUniLocale('en-US')).toBe('en');
~~~

HTTP 测试模拟 uni.request，断言 Header 为外部规范 zh-CN/en-US。

- [x] **Step 3: 初始化 Vue I18n**

createI18n 使用 legacy: false、fallbackLocale: zh-CN、两套静态消息；初始值来自 uni.getLocale 经适配器规范化。setActiveLocale 同步 Vue I18n 与 uni.setLocale，并监听 uni.onLocaleChange。

- [x] **Step 4: 本地化应用配置**

locale/ 文件覆盖 app.name、导航标题和启动说明；manifest 默认 zh-Hans。小程序动态页面标题使用 uni.setNavigationBarTitle；需要运行时切换的 tabBar 使用自定义实现，不调用平台不支持的动态原生 tabBar 文案接口。

- [x] **Step 5: 接入账号偏好与错误**

匿名选择立即本地持久化；登录后只有通过守卫的 `/api/v1/me` PreferredLocale 决定活动语言；已认证切换携带 ProfileVersion 调用 PUT `/api/v1/me/locale`，只有服务端响应通过守卫后才提交本地语言和版本，失败保留原语言、版本、会话与租户。ProblemDetails 优先用 violations.code/arguments 本地化，未知 code 使用服务端 title 并展示 traceId。

- [x] **Step 6: 三目标验证**

Run: pnpm --filter @fullnet/uniapp test

Run: pnpm --filter @fullnet/uniapp build:h5

Run: pnpm --filter @fullnet/uniapp build:mp-weixin

Run: pnpm --filter @fullnet/uniapp build:mp-alipay
Expected: 单元测试和三目标构建均退出 0；构建产物无未锁定远程语言资源。

- [ ] **Step 7: 平台冒烟**

H5、微信开发者工具、支付宝小程序开发者工具分别验证中文启动、切换英文、重启保持、登录/API Header、验证错误、会话失效和导航标题。每个平台保存版本和结果到 docs/verification/uniapp-localization.md。

当前证据：H5 已通过 5 项 Playwright 自动冒烟；微信与支付宝开发者工具均为 `Not executed — required tool not installed`，因此本步骤保持未完成，L3/C3 只能标记为 `Implementing / Build-verified`。

- [ ] **Step 8: 提交**

~~~powershell
git add clients/uniapp package.json pnpm-lock.yaml docs/verification/uniapp-localization.md
git commit -m "feat: add uniapp localization foundation"
~~~

### Task 7: 建立 Flutter 移动/桌面多语言基础

**Files:**
- Create: clients/flutter/pubspec.yaml
- Create: clients/flutter/l10n.yaml
- Create: clients/flutter/lib/main.dart
- Create: clients/flutter/lib/app/full_net_app.dart
- Create: clients/flutter/lib/l10n/app_en.arb
- Create: clients/flutter/lib/l10n/app_zh_CN.arb
- Create: clients/flutter/lib/localization/locale_controller.dart
- Create: clients/flutter/lib/api/locale_interceptor.dart
- Create: clients/flutter/test/localization/locale_controller_test.dart
- Create: clients/flutter/test/localization/app_localization_test.dart
- Create: clients/flutter/test/api/locale_interceptor_test.dart

**Interfaces:**
- Produces: 生成 AppLocalizations、LocaleController、Accept-Language 拦截器。
- Consumes: Task 1 规范语言和 Task 4 PreferredLocale。

- [ ] **Step 1: 固定 Flutter 稳定工具链**

使用实施时 Flutter 官方 stable 创建 Android/iOS/Windows/macOS/Linux 工程，将精确 SDK 版本记录到仓库工具链文件与 docs/development/getting-started.md；不使用 beta/dev/master。

- [ ] **Step 2: 写 RED Widget/单元测试**

测试 zh-CN/en-US 两种 Locale 的应用标题、登录按钮、ProblemDetails 回退、长文本布局、2.0 textScaleFactor，以及请求 Header。

- [ ] **Step 3: 配置 gen_l10n**

pubspec 启用 generate: true，并依赖 flutter_localizations SDK 与 intl。l10n.yaml 固定：

~~~yaml
arb-dir: lib/l10n
template-arb-file: app_en.arb
output-localization-file: app_localizations.dart
untranslated-messages-file: build/untranslated_messages.json
synthetic-package: false
~~~

app_en.arb 为生成模板并包含 @@locale: en_US，所有占位符提供 description/type；app_zh_CN.arb 包含 @@locale: zh_CN 并拥有相同键。

- [ ] **Step 4: 接入 MaterialApp 与偏好**

FullNetApp 使用 AppLocalizations.localizationsDelegates、supportedLocales 和 onGenerateTitle。LocaleController 先读本地明确选择，再读账号 PreferredLocale，再跟随 PlatformDispatcher locale，最后 zh-CN。

- [ ] **Step 5: 接入 HTTP**

LocaleInterceptor 每次请求发送规范 zh-CN/en-US。Dart 业务逻辑只依赖 ProblemDetails code/violations；服务器 title 是未知键回退。

- [ ] **Step 6: 平台资源**

分别本地化 Android/iOS/Windows/macOS/Linux 的应用名和系统可见元数据。平台不支持动态更改的名称在安装/系统语言变化后按平台行为验证，不伪造运行时能力。

- [ ] **Step 7: 运行验证**

Run: flutter gen-l10n

Run: flutter analyze

Run: flutter test

Run: flutter build apk --debug

Run: flutter build windows --debug
Expected: 生成无缺失消息，分析/测试通过；具备 Android SDK 的 Windows 节点完成 Android 与 Windows 构建，iOS/macOS/Linux 在对应构建节点执行后才声明支持。

- [ ] **Step 8: 提交**

~~~powershell
git add clients/flutter docs/development/getting-started.md
git commit -m "feat: add flutter localization foundation"
~~~

### Task 8: 规范业务翻译表、通知、Realtime 与 AI 语言边界

**Files:**
- Create: docs/architecture/localized-content-and-messaging.md
- Modify: docs/superpowers/specs/2026-07-17-technology-integration-roadmap-design.md
- Modify: docs/roadmap/adminnet-feature-parity.md
- Test: 对应实际模块的 SQL Server/MySQL 集成测试、通知渲染测试和协议测试

**Interfaces:**
- Produces: LocalizedContent、NotificationRenderContext 与 AgentOutputLocale 的跨模块设计边界。
- Consumes: Task 1 Locale、Task 4 用户/租户偏好；不提前创建没有消费者的通用表。

- [ ] **Step 1: 在首个真实消费者前写独立规格**

菜单、字典、公告、邮件、报表、Realtime 或 AI 中哪个首先进入实施，就为该模块写独立规格。规格必须明确实体、TenantId、EntityId、Locale 唯一约束、默认语言、发布版本和删除行为。

- [ ] **Step 2: 为所属模块写 RED 测试**

同一实体 zh-CN/en-US 命中、缺失语言回退、跨租户拒绝、并发版本冲突、缓存 locale 隔离；通知并行渲染不同语言不串文化。

- [ ] **Step 3: 实现模块自有 translation 表**

禁止跨模块通用 EAV。业务表和 translation 表在同一模块迁移中创建，SQL Server/MySQL 同时实现。FusionCache key 包含 locale，tag 同时覆盖实体所有语言。

- [ ] **Step 4: 实现异步显式语言**

Outbox 保存稳定事件数据；通知命令包含 RecipientId、TemplateKey、TemplateVersion、Locale 和参数快照。Realtime 优先发送 code/data。AI 输出请求显式指定 locale，但 Tool/Schema/权限 code 保持不变。

- [ ] **Step 5: 提交每个真实模块**

每个模块按 fullnet-module-delivery Skill 独立提交，不把未来模块的空表、空接口或未消费资源预先放入核心。

### Task 9: 完善 CI、文档和发布声明

**Files:**
- Modify: .github/workflows/ci.yml
- Modify: README.md
- Modify: docs/development/getting-started.md
- Modify: docs/roadmap/client-delivery-roadmap.md
- Modify: docs/roadmap/adminnet-feature-parity.md
- Modify: THIRD-PARTY-NOTICES
- Create: docs/verification/localization-release-checklist.md

**Interfaces:**
- Consumes: Tasks 1-8 的真实测试和构建结果。
- Produces: 可审计的支持语言、平台状态和发布门禁。

- [ ] **Step 1: 扩展 CI**

CI 固定运行 localization contract、后端单元/双库集成、Vue/Layui 单元/构建/E2E、uni-app 三目标构建、Flutter analyze/test 与具备节点的平台构建。未配置 macOS/Linux 节点的平台保持 Designing，不写成支持。

- [ ] **Step 2: 更新使用文档**

getting-started 记录 Accept-Language 示例、语言切换、本地资源命令、uni-app/Flutter 构建和缺失键排查。README 区分管理壳层现状与全栈完成状态。

- [ ] **Step 3: 更新许可证**

登记 Vue I18n、Flutter intl/flutter_localizations、DCloud 实际发布依赖和任何新增工具；区分开发测试依赖与最终分发依赖。

- [ ] **Step 4: 执行完整验证**

Run: dotnet build Full.NET.slnx --configuration Release --no-restore

Run: 四套 Microsoft.Testing.Platform 测试 DLL

Run: pnpm test:workspace

Run: pnpm test:localization

Run: pnpm test:clients

Run: pnpm build:clients

Run: pnpm test:e2e

Run: uni-app 三目标构建

Run: flutter gen-l10n && flutter analyze && flutter test
Expected: 所有已维护范围通过，测试数量不低于实施前门槛，未执行平台明确列为未验证。

- [ ] **Step 5: 执行规则与 Skills 复盘**

按 rules/rule-evolution.md 和 rules/skill-evolution.md 查重。只有跨至少两个真实任务稳定复用后才升级 fullnet-localization-delivery Skill；单次环境差异或平台工具故障只记录验证证据。

- [ ] **Step 6: 提交**

~~~powershell
git add .github README.md docs THIRD-PARTY-NOTICES
git commit -m "docs: record fullstack localization delivery"
~~~

## Execution Order and Checkpoints

1. 第一批：Task 1-3，形成不依赖数据库偏好的 API 多语言闭环；
2. 第二批：Task 4-5，形成账号/租户偏好与双管理端完整闭环；
3. 第三批：Task 6，独立交付 uni-app 三目标；
4. 第四批：Task 7，独立交付 Flutter，平台构建按节点逐步升格；
5. 持续批：Task 8 随真实模块进入；Task 9 在每批结束时更新真实状态。

每一批都必须在自己的功能分支和隔离工作树执行，通过审查后本地合并；禁止为了快速宣称“全栈多语言”一次性并行修改全部平台而缺少可运行中间状态。
