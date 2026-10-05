# 创建首个 CRUD 与环境诊断

本教程提供 Minimal 独立应用的只读诊断与最小租户 CRUD 示例。代表性生成 CRUD 的数据库、OpenAPI、Vue、精确权限、跨租户拒绝、再生成及升级恢复已完成本地双库验收，见[总计划 F02](../superpowers/plans/2026-09-16-foundation-productization.md#f02环境诊断与生成一个真实-crud)。Demo/acme 七步教程已在同一保留的独立应用逐步实走，运行与分段验收边界见第七步；完整配置诊断仍待收口，教程通过不表示业务已经上线。

## 前置条件

- 已安装 .NET 10 SDK（`dotnet --version` 可执行）
- 已安装 Node.js 24 与模板 `packageManager` 指定的 pnpm 10.26.0，创建器和诊断脚本需要这两个入口可执行
- 独立应用根目录包含 `fullnet-app.json`、`framework-manifest.json`、`src/<name>.Host.Api`、`src/<name>.Host.Worker`、`src/<name>.Host.Migrator` 与 `framework/fullnet/`；`src/Composition`、`src/Hosts`、`src/Modules` 是原框架仓库的布局
- `appsettings.json` 已配置 `FullNet:Modules:Preset`（如 `minimal` 或 `platform`）

## 从空目录创建示例应用

先在原 Full.NET 仓库根目录执行以下命令。输出目录必须不存在，包目录必须为空；再次演练请选用新的目录，不覆盖已有应用。

```bash
node scripts/templates/build-app-template.mjs --output artifacts/first-crud/package
node artifacts/first-crud/package/.fullnet-tools/create-app.mjs --output artifacts/first-crud/Demo --name Demo --owner-key acme --database sqlserver --preset minimal --http-port 5198
cd artifacts/first-crud/Demo
```

后续命令全部从新应用根目录执行。此例固定应用名 `Demo` 与 OwnerKey `acme`，下方 Schema 与接入路径使用同一组名称；MySQL 示例把创建参数改为 `--database mysql`。使用验证过的创建器后，应用 `package.json` 包含两个 diagnose 脚本；它们使用随应用分发的 CLI，不依赖原仓库的工作目录。已有应用不会因框架源码升级自动获得应用自有脚本，可使用下方等价 CLI 命令，并显式采纳这两个入口。

## 第一步：运行 diagnose

在应用根目录执行：

```bash
pnpm run diagnose:development

# 等价入口；以下命令均从独立应用根目录执行
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- diagnose --workspace . --profile development
```

成功输出包含机器可读行，例如：

```
DIAG_SDK_OK ok 检测到 .NET SDK 10.x.x。
DIAG_WORKSPACE_OK ok 工作区结构符合 Full.NET 应用布局。
DIAG_MODULES_OK ok 已配置 FullNet:Modules 模块预设或启用列表。
```

开发环境若尚未配置数据库连接，可能出现 `DIAG_CONNECTION_PLACEHOLDER warn ... hint=...`；可按 hint 设置环境变量。数据库连接先取最终生效的 `Database:ConnectionString`，仅当其为 null、空串或空白时，才回退到 `Database:ConnectionName` 指向的 `ConnectionStrings:<name>`；非空直配中的占位符不能被命名连接掩盖。若使用 user-secrets，须先在应用 API 项目初始化 `UserSecretsId`，并写入直配键或目标命名连接键；仅有 `secrets.json` 文件或其他连接键不算已配置。默认宿主只在 Development 载入 User Secrets，Production 必须通过部署密钥或环境变量提供连接。诊断检查最终所选连接是否非空、非占位，**不会输出连接字符串原文**。

JSON 中的非空对象或数组展平为子键，不会自动覆盖较低优先级的同路径标量。需要命名连接时，未出现 `Database:ConnectionName` 标量才保留宿主默认名 `fullnet`；显式 null、空串或空白不能恢复该默认名。连接名的数字、布尔标量按宿主转换后的字符串匹配，诊断保持不回显。

生产配置使用 `pnpm run diagnose:production` 或 `--profile production`；缺少连接或秘密占位符将报告 `error` 并以非零退出码结束。Profile 只接受 `development`、`production`，重复或未知参数拒绝执行。

诊断按目标工作区的 `global.json` 解析 SDK，按当前分发基线接受 10.0.100 及更高的 10.0 SDK 功能带；SDK 选择及预览版准入由 .NET 自身解析，其他主/次版本不自动认证为兼容。版本格式无效时输出脱敏错误，合法后缀也只标注预览状态，不回显任意后缀内容。该检查不替代真实构建、工作负载或 Native AOT 工具链验收。诊断检查宿主 `appsettings.json`、独立应用清单、所选模块引用及配置占位符。基础与所选环境 appsettings JSON 均允许宿主支持的注释和尾逗号；配置根须为对象，标量展平路径按不区分大小写检查冲突，包括扁平/嵌套键与数组索引。高优先级有效值不能掩盖坏配置文件。独立应用的根、API及已声明同名Worker/Migrator的基础JSON，其模块预设和数据库Provider必须都与冻结档案一致；相关文件缺失、无效或字段类型错误会返回脱敏错误，不能由API/根配置回退掩盖。无Migrator的旧应用仍可诊断，不会自动创建宿主。User Secrets 仅在 API 项目具有有效 `UserSecretsId` 时检查直配或目标命名连接键，支持扁平与嵌套 JSON；不可读取或无效的秘密文件按未配置处理。诊断不会执行初始化、迁移或数据库连接，也不证明配置中的地址可达；它不是完整ASP.NET Core配置加载器，不认证部署环境的全部覆盖来源。

新创建应用的 `pnpm run diagnose:development` / `pnpm run diagnose:production` 先通过应用自带的 Node 入口探测目标工作区 SDK。找不到 `dotnet` 或无法选择 SDK 时输出固定 `DIAG_SDK_MISSING`；不兼容版本输出 `DIAG_SDK_INCOMPATIBLE`，均退出 1，不输出原始 SDK 错误或版本后缀，也不安装 SDK。SDK 可用后继续运行现有 .NET CLI，保留其诊断和退出码；前置成功不表示应用配置通过。探测等待上限为 30 秒，不保证整条命令或进程清理在该时间内完成，Node 前置检查也不承诺回收派生进程树。已有应用的旧脚本和手工诊断脚本不会自动替换；直接运行 `dotnet run ... diagnose` 仍须先具备可启动的 SDK。

基础命名连接按宿主展平路径读取，支持扁平/嵌套键、不区分大小写的路径及合法子路径；显式空值覆盖不恢复基础凭据，非空子键不抹除同路径标量。命名凭据仍按文本判断，不把数字/布尔值计为已配置。诊断保持已有基础凭据字段类型校验，不认证任意配置结构或连接串语法。

三个常见秘密键的基础值也按展平路径读取，支持扁平/嵌套键及大小写变体。未声明键不强制存在；显式 null、空白或空集合覆盖视为占位，非空子键不抹除同路径标量。保留已有基础秘密字段类型校验，非文本秘密不能据此计为有效凭据。此检查不验证 Redis 可达性或 SM2 密钥格式。

Identity 是合法模块组合的必需模块，未声明该节时仍按默认启用令牌端点检查签名配置。缺少匹配的 `ActiveKeyId` 或同名 `SigningKeys` 的 `PublicKeyPem`、`PrivateKeyPem`，以及模板占位值，会报告 `DIAG_IDENTITY_SIGNING_REQUIRED`：Development 为 warning，Production 为 error。KeyId 必须与合并后的字典键名大小写精确一致；诊断不回显 KeyId 或密钥。显式关闭 `EnableTokenEndpoints` 时不要求签发用的活动密钥；其显式 JSON null 或空对象按 .NET 10 绑定为 false，缺键才保留默认 true。

仅 Development 可显式启用 `Identity:AllowDevelopmentEphemeralSigningKey=true`；诊断报告 `DIAG_IDENTITY_EPHEMERAL_SIGNING warn`，提醒重启会使令牌失效。Production 启用该开关始终为 error，即使关闭了令牌端点。两个开关不能绑定为布尔值时报告 `DIAG_IDENTITY_SIGNING_OPTIONS_INVALID error`。签名配置按环境变量、Development User Secrets、所选环境 JSON、基础 JSON 的优先级逐叶读取，空父节点不会抹除低层子键；Production 不读取 Development User Secrets。`DIAG_IDENTITY_SIGNING_CONFIGURED ok` 只表示活动密钥配置项齐全，未验证 PEM 格式、密钥配对或密码学有效性，也不代替整个 Identity Options 或宿主启动验证。

独立应用的冻结档案检查也按展平路径读取各基础文件中的 `FullNet:Modules:Preset`、`Database:Provider` 和 Worker 的 `Kestrel:Endpoints:Http:Url`，支持扁平键和大小写变体。空集合覆盖不能保留旧预设或旧端口；非空子键不抹除同路径标量。该检查仍核对基础文件与应用清单的一致性，不用环境覆盖修复基础档案漂移，也不替代完整宿主启动验证。

OIDC 默认关闭，诊断报告 `DIAG_OIDC_DISABLED ok`；关闭后不要求其独立签名配置。启用 `Identity:Oidc:Enable` 后，必须配置 `ActiveSigningKeyId` 及同名 `SigningKeys` 的公私钥，JWT 密钥或 JWT 临时签名开关不能替代。缺失、占位值或 KeyId 大小写不匹配报告 `DIAG_OIDC_SIGNING_REQUIRED`，Development 为 warning、Production 为 error；开发显式启用 OIDC 临时签名报告 `DIAG_OIDC_EPHEMERAL_SIGNING warn`，已启用的生产 OIDC 使用临时签名为 error。不能绑定的布尔开关报告 `DIAG_OIDC_SIGNING_OPTIONS_INVALID error`，不会因关闭 OIDC 而掩盖绑定错误。覆盖顺序与 KeyId 拼写合并遵循前述签名配置边界，Production 不读取开发秘密。`DIAG_OIDC_SIGNING_CONFIGURED ok` 仅表示 OIDC 活动签名配置齐全，不认证 PEM、Issuer、客户端、加密配置或完整协议启动。

签名字典的 JSON null、空对象与空条目按实际配置绑定处理，不直接套用手工构造 Options 的 null 校验：已初始化字典保留，空条目被跳过；空父节点也不会删除较低层已有子键。只有活动签名配置确实缺失时才报告所需密钥，避免把合法关闭/开发配置误报为结构错误。JSON 标量文本转换也与配置提供程序一致，例如布尔值绑定到字符串为 `True` / `False`，随后仍按 KeyId 的精确拼写匹配；应用配置应使用明确的字符串 KeyId。

基础配置允许分段声明同名对象，只要展开后的标量路径不冲突；例如分别声明 `Database:Provider` 与 `Database:CommandTimeoutSeconds` 的两个 `Database` 对象。诊断保留所有片段及声明顺序，不会因对象名重复误拒绝合法配置。重复标量路径、扁平/嵌套冲突及数组索引冲突仍会失败；合法空集合覆盖与原有字段类型约束继续生效，冻结清单的 JSON 规则不因此放宽。

启用 OIDC 后，Issuer 必须是无用户凭据的 HTTP(S) 绝对地址；缺失或格式错误在 Development/Production 均报告 `DIAG_OIDC_ISSUER_INVALID error`，通过仅报告 `DIAG_OIDC_ISSUER_CONFIGURED ok`，不会访问或回显地址。可选的 `EncryptionKeyBase64` 保留现有空值语义；非空时须为解码后恰好 32 字节的 Base64，否则报告 `DIAG_OIDC_ENCRYPTION_INVALID error`，通过为 `DIAG_OIDC_ENCRYPTION_CONFIGURED ok`。这些结果仅表示配置格式通过，不认证密钥强度、多实例一致性、客户端注册、真实 PEM 或完整协议启动。关闭 OIDC 时不检查这两项；来源覆盖、生产忽略开发秘密、只读与脱敏边界保持。

启用 OIDC 后还检查固定客户端注册：没有可绑定的客户端、ClientId 为空或按 Ordinal 重复、RedirectUris 缺失，或回调地址含通配符、用户凭据、片段及去掉尾斜线后重复，均报告 `DIAG_OIDC_CLIENTS_INVALID error`，Development/Production 一致。通过报告 `DIAG_OIDC_CLIENTS_CONFIGURED ok`；HTTP(S) 绝对回调的查询参数仍按现有宿主规则允许，PostLogoutRedirectUris 可为空。配置按前述四层来源逐叶合并；空父节点不删除低层子键，数组中的不可绑定项按 .NET Binder 行为处理。关闭 OIDC 不检查客户端；诊断不回显 ClientId、回调地址或 ClientSecret，不认证客户端秘密、其他选项、地址可达性或完整协议启动。

模块声明提示与预设取值检查分开：`DIAG_MODULES_OK` 仍仅表示基础配置有声明；未采用显式 Enabled 列表时，诊断按四层来源读取最终 `FullNet:Modules:Preset`，缺键保留宿主默认 Full，名称忽略大小写但不忽略空白。未知、空白、显式 null 或空集合覆盖后的无效预设，在两种 Profile 均报告 `DIAG_MODULE_PRESET_INVALID error`；通过为 `DIAG_MODULE_PRESET_CONFIGURED ok`，不回显配置值。Enabled 的非空标量不能绑定为列表，仍检查 Preset；数组子键或空字符串数组标记可以绑定列表。显式 Enabled（含空数组）覆盖 Preset，空父节点不删除低层数组子键，本检查不认证列表名称、空集、依赖 DAG、预设成员或宿主启动；这些仍需对应运行验证。

## 第二步：准备 CRUD Schema

原框架仓库的示例主从单据见 [`samples/enterprise-request/schema.json`](../../samples/enterprise-request/schema.json)（`master.detail` 场景：申请头 + 明细行）。应用应准备自己的 `schema.json`，冻结项目 OwnerKey，并显式声明字段、精确权限与 `dataScope`；不能直接沿用原仓库的集成目标路径。

为上述 `Demo` 示例，在应用根保存以下内容为 `schema.json`。产品使用租户上下文、应用端 UUID v7、版本并发检查与服务端创建审计；`TenantId` 和审计字段不是客户端可写字段。硬删除是此样例的显式策略，业务项目应先确定自己的删除语义。

```json
{
  "ownerKey": "acme",
  "moduleKey": "catalog",
  "entityKey": "product",
  "databaseTableName": "acme_catalog_product",
  "rootNamespace": "Demo.Modules.Catalog",
  "clrTypeName": "Product",
  "apiResourceName": "products",
  "permissionResourceName": "products",
  "dataScope": "tenant.required",
  "entityCapabilities": {
    "deleteMode": "hard.delete",
    "hasCreatedAudit": true,
    "hasUpdatedAudit": false,
    "hasDeletedAudit": false,
    "hasVersion": true,
    "ownershipMode": "none"
  },
  "columns": [
    { "databaseName": "Id", "clrPropertyName": "Id", "jsonPropertyName": "id", "scalarType": "Uuid" },
    { "databaseName": "TenantId", "clrPropertyName": "TenantId", "jsonPropertyName": "tenantId", "scalarType": "Uuid" },
    { "databaseName": "Name", "clrPropertyName": "Name", "jsonPropertyName": "name", "scalarType": "String", "maxLength": 200 },
    { "databaseName": "Version", "clrPropertyName": "Version", "jsonPropertyName": "version", "scalarType": "Int64" },
    { "databaseName": "CreatedAtUtc", "clrPropertyName": "CreatedAtUtc", "jsonPropertyName": "createdAtUtc", "scalarType": "DateTimeUtc" },
    { "databaseName": "CreatedById", "clrPropertyName": "CreatedById", "jsonPropertyName": "createdById", "scalarType": "Uuid" }
  ]
}
```

## 第三步：预览生成计划

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- --schema schema.json --workspace .
```

输出 `Create`/`Update`/`Unchanged` 行，默认不写盘。空应用中的上述 Schema 应产生 14 个 `Create`，包含后端、Vue、OpenAPI 与两库迁移草案；此时不应出现 `backend/`、`clients/` 或 `.fullnet/codegeneration-manifest.json`。

## 第四步：应用生成

确认计划后追加 `--apply`：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- --schema schema.json --workspace . --apply
```

相同输入重复执行应报告 `Unchanged`；未登记的人工文件应保留，人工修改的受管产物应报告冲突并拒绝覆盖。生成产物落盘不等于模块已接入宿主或可运行。

可以在新应用中用下面的 PowerShell 步骤验证保护边界。第一遍生成后再添加人工文件，重复生成应仍报告 14 个 `Unchanged`，且人工文件内容保持。

```powershell
'// 人工业务扩展，重复生成必须保留。' | Set-Content -Encoding utf8 backend/Product.manual.cs
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- --schema schema.json --workspace . --apply
Copy-Item backend/ProductSql.g.cs backend/ProductSql.before-conflict.txt
Add-Content backend/ProductSql.g.cs '// 人工修改，必须拒绝覆盖。'
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- --schema schema.json --workspace . --apply
```

最后一条命令应返回退出码 2，并精确报告 `Conflict backend/ProductSql.g.cs`；修改后的 SQL、人工文件与生成清单均应保持，其他产物也不能被部分更新。只有确认备份属于本次演练、期间未再修改 SQL，才能用 `Copy-Item backend/ProductSql.before-conflict.txt backend/ProductSql.g.cs` 撤销本次测试注释，再继续接入；业务修改应保留并人工评审，不应删除生成清单绕过冲突。

2026-10-04 已用修复提交 `0957ee6232cd4ccebf3f90847d77955675baa4d3` 创建新的 SQL Server / Minimal 应用，保存本文 Schema 后执行上述入口。开发诊断返回 0，并报告连接与三个常见秘密占位；生产诊断对同一占位配置返回 1。预览、生成、重复生成分别返回 0；14 个产物符合预期，人工文件保持，SQL 冲突返回 2，配置与受管框架摘要保持。六条 CLI 命令合计 40.214 秒，不含组包、创建、下载、宿主接入、迁移或启动耗时；它不是完整教程或冷启动时长承诺。原始结果位于 `.tmp/f02-tutorial-walk-0957ee62/results/`。此六命令证据只覆盖前四步，后续接线、迁移和运行实走记录另见第五至第七步。

独立应用的模板验收会用应用包内的 CLI 检查租户 CRUD 预览不写入产物、生成后相同输入为 `Unchanged`、人工文件保留，以及修改受管 SQL 后返回冲突且保持产物与清单字节。每阶段日志保存在 `.tmp/template-real-stack/application-crud/` 并由 Actions 上传；这项生成与保护检查不代替下方的模块接入、业务双库运行、权限或页面验收。

模板验收还会通过 `apply-module-integration` 将后端产物接入应用自有模块，经过候选编译后实际构建模块项目，并验证重复接入及人工 SQL 修改保护。日志位于 `.tmp/template-real-stack/application-crud-module/`。该阶段只验证模块编译和写盘保护，尚未将生成注册桥、授权贡献者和模块接入 API，也不执行业务迁移。

后续接线验收通过 CLI 将生成注册桥接入模块入口，将模块引用与实例加入应用自有 Composition，再构建 API 并重复接入检查 `Unchanged` 和源码字节。日志位于 `.tmp/template-real-stack/application-crud-host-wiring/`。为进入此阶段，冲突负例确认内容保留后会显式撤销验收自己追加的模块 SQL 测试注释；根目录的人工 SQL 与人工业务文件继续保留。API 编译仍不证明运行期 DI、授权、HTTP、业务双库或页面通过。

接线之后的运行探针复用当前应用启动装配代码，显式开启 DI 构建和作用域校验，检查两个生成服务的实例隔离、五条路由的精确权限元数据及 DTO 长整数 JSON 往返。日志位于 `.tmp/template-real-stack/application-crud-runtime/`。探针在应用自己的 `verification/CrudRuntimeProbe` 中构建，不修改 API 入口，不启动监听、后台服务或数据库；HTTP、迁移与双库业务仍需后续验收，探针成功也不代表生产配置或 Native 发布通过。

授权目录验收在运行探针之前创建应用拥有的无状态贡献者，以 Singleton 注册，使用不带 `clientRoute` 的显式授权目标执行完整 Host CLI 接入。生成四项 Tenant 权限、一项页面导航和三项操作后，追加验收自己的人工注释并重复接入，检查人工权限、所有相关产物与 Vue 路由字节。日志位于 `.tmp/template-real-stack/application-crud-authorization/`。运行探针随后通过实际 `IAuthorizationPolicyProvider` 物化权威目录，检查生成、人工与官方策略，以及未知权限拒绝；策略解析成功仍不代表真实请求已完成授权。

运行探针还通过实际 `IAuthorizationService` 执行四项生成策略：租户精确权限与租户超级管理员允许，缺权限、匿名、Host、Host超级管理员及缺失/非法作用域拒绝；四权限两两交叉，只允许同项权限。48次实际结果累计为12允许、36拒绝并进入报告，缺失执行结果的旧报告拒绝。这些主体由验收构造Claims，模拟认证后的授权输入，不替代JWT签名、会话、安全戳、HTTP中间件与跨租户数据隔离验证，亦未将构造主体用于应用请求。

## 第五步：模块接入（可选）

### 创建应用自有 Catalog 模块

下面接着前四步的 `Demo` 应用执行，命令的工作目录始终是应用根目录。创建 `src/Demo.Modules.Catalog/`，将以下内容保存为该目录中的 `Demo.Modules.Catalog.csproj`。应用根的 `Directory.Build.props` 已导入冻结框架的编译基线；模块只引用所需边界，Identity 使用 Contracts 项目。

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <ProjectReference Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Abstractions/Full.NET.Abstractions.csproj" />
    <ProjectReference Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj" />
    <ProjectReference Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj" />
    <ProjectReference Include="../../framework/fullnet/src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj" />
    <ProjectReference Include="../../framework/fullnet/src/Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj" />
  </ItemGroup>
</Project>
```

同目录保存 `CatalogModule.cs`：

```csharp
using Full.NET.Modularity.Modules;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Demo.Modules.Catalog;

/// <summary>应用自有目录模块；生成业务入口由显式接入命令装配。</summary>
public sealed class CatalogModule : IFullNetModule
{
    public string Name => "Catalog";
    public IReadOnlyCollection<string> Dependencies => ["Identity"];

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
```

在应用根保存 `integration-target.json`：

```json
{
  "moduleName": "Catalog",
  "moduleProjectPath": "src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj",
  "moduleEntryPointPath": "src/Demo.Modules.Catalog/CatalogModule.cs",
  "compositionProjectPath": "src/Demo.Composition/Demo.Composition.csproj",
  "compositionCatalogPath": "src/Demo.Composition/ApplicationModuleCatalog.cs",
  "vueRouterPath": "ui/admin/src/router/index.ts"
}
```

规划命令只报告接线缺口，不写文件；此时后端生成和宿主接线显示 `ChangeRequired`，已有模块项目显示 `Satisfied`，未声明客户端路由显示 `ManualReview`。然后生成模块内的六个产物并编译：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration --schema schema.json --repository . --target integration-target.json
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-module-integration --schema schema.json --repository . --target integration-target.json
dotnet build src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj -c Release
```

`apply-module-integration` 应报告六项 `Create` 和模块候选编译通过；产物位于模块的 `Generated/`，原来根目录的 `backend/` 草稿保持。此命令尚未接入 Composition、授权贡献者或页面。

### 接入授权贡献者与组合根

模块生成完成后，在同目录保存 `CatalogAuthorizationContributor.cs`。三个标准集合供 CLI 接入，保留一项人工权限用于确认再生成不会覆盖应用扩展：

```csharp
using Demo.Modules.Catalog.Generated;
using Full.NET.Modules.Identity.Contracts;

namespace Demo.Modules.Catalog;

/// <summary>目录模块的静态授权目录，不持有请求或租户上下文。</summary>
public sealed class CatalogAuthorizationContributor : IAuthorizationCatalogContributor
{
    public AuthorizationModuleDefinition Module { get; } = new("catalog", "应用目录", 200);

    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new PermissionDefinition("catalog.manual.read", "人工权限", AuthorizationScope.Tenant),
    ];

    public IReadOnlyCollection<NavigationDefinition> Navigation { get; } = [];
    public IReadOnlyCollection<AuthorizationActionDefinition> Actions { get; } = [];
}
```

在 `CatalogModule.AddServices` 的 `services.AddOptions();` 后添加：

```csharp
services.AddSingleton<Full.NET.Modules.Identity.Contracts.IAuthorizationCatalogContributor, CatalogAuthorizationContributor>();
```

另存应用根的 `host-target.json`；逐阶段目标和完整编排目标必须分开，因为只有完整编排接受 `authorizationContributorPath`：

```json
{
  "moduleName": "Catalog",
  "moduleProjectPath": "src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj",
  "moduleEntryPointPath": "src/Demo.Modules.Catalog/CatalogModule.cs",
  "compositionProjectPath": "src/Demo.Composition/Demo.Composition.csproj",
  "compositionCatalogPath": "src/Demo.Composition/ApplicationModuleCatalog.cs",
  "vueRouterPath": "ui/admin/src/router/index.ts",
  "authorizationContributorPath": "src/Demo.Modules.Catalog/CatalogAuthorizationContributor.cs"
}
```

执行完整后端接入、API 编译，再重复接入：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-host-integration --schema schema.json --repository . --target host-target.json
dotnet build src/Demo.Host.Api/Demo.Host.Api.csproj -c Release
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-host-integration --schema schema.json --repository . --target host-target.json
```

两次编排都应报告 `Applied HostIntegration`；检查重复执行前后文件内容保持，不能仅凭同一成功消息认定无漂移。Composition 应只引用并注册一次 Catalog，模块入口应注册生成服务并映射生成端点。贡献者保留人工权限，增加四项 Tenant 权限、一项导航和三项操作；硬删除仍沿用稳定的 `catalog.products.disable` 权限。API、Worker、Migrator 继续各用自己的 Host Profile。

此目标未声明 `clientRoute`，所以本步只接后端。API 编译不证明 HTTP 权限、数据库可用、Vue 页面或 Native AOT 通过；业务 SQL 草稿还必须按后文显式接入应用 Migrator，不能直接启动 API 后将缺表错误计为教程成功。

2026-10-05 实走：续用前四步从源码 `0957ee6232cd4ccebf3f90847d77955675baa4d3` 冻结创建的 SQL Server / Minimal 应用，直接取本文 XML、C#、JSON 和命令完成本步。六条命令及一次人工注释后的额外重复接入全部退出 0，实走与文件核对合计 128.338 秒；模块与 API 的 Release 编译均为 0 警告、0 错误。规划不写盘，生成六产物，Catalog 单次引用/注册，贡献者四项 Tenant 权限、一项导航、三项操作，人工权限和注释、配置、原草稿及全部框架摘要保持。初次规划对照暴露教程原有 `Missing/Ready` 说明错误，现按实际状态更正。结果留存 `.tmp/f02-tutorial-host-0957ee62-run3/`；这是后端接线与编译证据，该应用尚未启动数据库、API 监听、Worker 或浏览器，完整教程仍未验收。

### 接入业务客户端与 Vue 页面

继续使用已完成后端接入的同一个 Demo。本节采纳的是应用拥有的客户端与管理端文件；`framework/fullnet/` 不参与修改，后台交付仍只接 Vue。

在应用的 `contracts/openapi/catalog-product.client-manifest.json` 保存以下内容。五个产品操作都受保护，所以不登记任何公开操作：

```json
{ "publicOperationIds": [] }
```

确认 `packages/client-contracts/src/application-generated/catalog-product/` 尚不存在，然后从根目录执行：

```bash
node .fullnet-tools/openapi/generate-fullnet-client.mjs --input contracts/openapi/products.generated.openapi.json --manifest contracts/openapi/catalog-product.client-manifest.json --output packages/client-contracts/src/application-generated/catalog-product --http-module @fullnet/client-contracts
node .fullnet-tools/openapi/generate-fullnet-client.mjs --input contracts/openapi/products.generated.openapi.json --manifest contracts/openapi/catalog-product.client-manifest.json --output packages/client-contracts/src/application-generated/catalog-product --http-module @fullnet/client-contracts --check
```

应生成四个 TypeScript 文件并通过零漂移检查。业务产物与原 `src/generated/` 基线分开；`--http-module` 使业务操作复用共享 HTTP 类型。这里消费的是 CRUD 生成的静态 OpenAPI，尚未与正在运行的 API 文档比较。客户端生成器的重新生成会写入指定目录，不提供人工合并；这些文件保持生成器所有，人工扩展放在其他文件，`--check` 只核对且不写盘。

在 `packages/client-contracts/src/index.ts` 末尾**仅追加一次**以下导出，保留现有认证、租户与 HTTP 导出：

```typescript
export {
  catalogCreateProduct,
  catalogDeleteProduct,
  catalogGetProduct,
  catalogListProducts,
  catalogUpdateProduct
} from './application-generated/catalog-product/operations.generated.js';

export type {
  CreateProductRequest,
  DeleteProductRequest,
  PagedResultOfProductResponse,
  ProductResponse,
  UpdateProductRequest
} from './application-generated/catalog-product/models.generated.js';
```

把已生成的三个 Vue 文件采纳到页面目录。下面是 PowerShell 命令，先核对全部目标不存在，避免覆盖已有页面：

```powershell
$vueTargets = @(
  'ui/admin/src/views/products-page.generated.ts',
  'ui/admin/src/views/products.generated.ts',
  'ui/admin/src/views/CatalogProductsView.vue'
)
foreach ($vueTarget in $vueTargets) {
  if (Test-Path -LiteralPath $vueTarget) { throw "页面目标已存在，先审查：$vueTarget" }
}
Copy-Item -LiteralPath clients/vue/products-page.generated.ts -Destination ui/admin/src/views/products-page.generated.ts
Copy-Item -LiteralPath clients/vue/products.generated.ts -Destination ui/admin/src/views/products.generated.ts
Copy-Item -LiteralPath clients/vue/productsView.vue -Destination ui/admin/src/views/CatalogProductsView.vue
```

在 `packages/client-contracts/src/navigation-catalog.ts` 的 `ADMIN_NAVIGATION_CATALOG` 数组末尾加入下面一项，并给前一项补逗号。先核对 componentKey、routeName 与 path 均未被其他项占用；保留其他导航及失败关闭校验，不增加公开路由或手写权限白名单：

```typescript
  {
    componentKey: 'm7-catalog-products',
    routeName: 'm7-catalog-products',
    path: '/catalog/products'
  }
```

该三元组必须与后端贡献者生成的导航一致；本地目录只登记可信组件，不授予权限。在应用根另存 `vue-target.json`，从逐阶段目标扩展 `clientRoute`，不带完整 Host 专用的 `authorizationContributorPath`：

```json
{
  "moduleName": "Catalog",
  "moduleProjectPath": "src/Demo.Modules.Catalog/Demo.Modules.Catalog.csproj",
  "moduleEntryPointPath": "src/Demo.Modules.Catalog/CatalogModule.cs",
  "compositionProjectPath": "src/Demo.Composition/Demo.Composition.csproj",
  "compositionCatalogPath": "src/Demo.Composition/ApplicationModuleCatalog.cs",
  "vueRouterPath": "ui/admin/src/router/index.ts",
  "clientRoute": {
    "routePath": "/catalog/products",
    "vueRouteName": "m7-catalog-products",
    "vueComponentPath": "ui/admin/src/views/CatalogProductsView.vue"
  }
}
```

执行路由接入及重复检查，再安装冻结依赖、编译共享包和 Vue：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-client-route-integration --schema schema.json --repository . --target vue-target.json
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-client-route-integration --schema schema.json --repository . --target vue-target.json
pnpm install --frozen-lockfile
pnpm --filter @fullnet/client-contracts build
pnpm --filter @fullnet/admin build
```

首次路由应报告 `Update ui/admin/src/router/index.ts`，重复应报告 `Unchanged` 且内容保持。Vue 构建包含类型检查；页面继续使用应用已有 HTTP、Session 和路由守卫，前端按钮隐藏不能替代服务端精确授权。本节至多证明接线与生产构建，数据库迁移、实际登录、租户切换及普通账号页面/操作仍须运行验收，不能标为教程全链路 `Verified`。

采纳的 `CatalogProductsView.vue` 不属于根 CRUD 生成清单，可在 `<script setup lang="ts">` 后追加人工注释，再执行第四步的 `--apply` 并比较页面、路由、客户端导出与导航文件，确认全部保持。对生成源 `clients/vue/productsView.vue` 的人工修改则应触发退出 2、精确 `Conflict`，并保持清单及所有产物；只撤销本次验证注释后再构建。该保护不表示生成器会自动同步或合并已采纳页面。

相关随包测试可从应用根执行 `pnpm --filter @fullnet/client-contracts test tests/navigation-catalog.test.ts --maxWorkers=2` 和 `pnpm --filter @fullnet/admin test src/navigation/catalog.test.ts src/router/index.auth-guard.test.ts --maxWorkers=2`；它们检查导航与守卫，仍不替代真实账号、数据库或浏览器验收。

2026-10-05 实走：续用源码 `0957ee62` 的同一独立应用，直接采纳本文 JSON、导出、导航项与 PowerShell 脚本，七条生成/接入/依赖/构建命令全部退出 0。包含额外保护复核的 13 次进程均符合预期：已有页面采纳拒绝退出 1，生成 Vue 源冲突退出 2，其余退出 0，实走与文件核对共 69.813 秒。四份客户端、五操作、三 Vue 文件接入完成；共享包编译、Vue 类型检查与生产构建、客户端零漂移检查通过。正确导航三元组接受，三个字段分别改错均拒绝；重复路由与 14 产物再生成保持，人工页面注释及后端、配置、锁文件和受管框架摘要不变。随后上述随包测试分别 3/3 与 8/8、零失败/跳过、退出 0，耗时分别 1.04 秒与 33.70 秒，单独计时。结果留存 `.tmp/f02-tutorial-vue-0957ee62/`；安装沿用既有脚本审批策略，未更改锁文件或审批依赖脚本。该应用仍未启动数据库、API 监听或浏览器，教程显式业务迁移与运行继续待办。

### 接入目标与命令边界

原仓库的 `samples/enterprise-request/integration-target.json` 是仓库布局示例，不适用于独立应用。准备应用自己的 `integration-target.json`，显式选择应用拥有的模块项目、入口与宿主接入位置；不得为了接入业务改写受管框架或恢复冻结 Layui 交付线。规划入口：

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- plan-module-integration \
  --schema schema.json \
  --repository . \
  --target integration-target.json
```

按 `ChangeRequired` 项完成对应接线；`Satisfied` 表示已满足，`ManualReview` 须人工复核，`Blocked` 须先解除阻断，再执行相应接入子命令。

只交付 Vue 的目标 JSON 可以省略 `layuiRouterPath`，`clientRoute` 可以只提供 `routePath`、`vueRouteName`、`vueComponentPath`。如显式提供存量 Layui 控制器，`layuiControllerPath` 与 `layuiControllerExport` 必须成对；此兼容读取能力不授权恢复 Layui 开发。未知字段、非法路径或不完整配对仍拒绝。

`apply-client-route-integration` 要求模块聚合桥已由生成清单拥有、模块入口和 Composition 已完成接入、Vue 组件已存在；条件不满足时拒绝写盘。Vue-only 目标仅修改 Vue 路由，重复执行报告 `Unchanged`，不创建 Layui 文件。该结构接入检查不能代替宿主运行、精确权限或页面验收。

生成的授权片段是 `Permissions`、`Navigation`、`Actions` 的集合元素，应分别接入应用拥有的授权贡献者；不能把片段直接追加到 C# 文件末尾。自动接入要求三个标准集合及完整生成块，部分标记、人工改动或结构歧义拒绝修改。

完整编排入口 `apply-host-integration` 依次执行后端、模块入口、Composition、可选 Vue 与授权贡献者接入。目标 JSON 必须额外显式提供 `authorizationContributorPath`（应用拥有的现有 C# 文件相对路径）；Contributor 的接口实现、DI 注册与所需 using 由应用声明。该字段仅用于完整编排命令，其他逐阶段命令仍拒绝它，包括显式 `null`，避免忽略授权目标。

```bash
dotnet run --project framework/fullnet/src/Tools/Full.NET.CodeGeneration.Cli -- apply-host-integration --schema schema.json --repository . --target host-target.json
```

上例从独立应用根目录执行；前面的 Catalog 示例已给出有效的 `host-target.json`、模块项目和贡献者，其他应用须按自己的名称与布局调整。命令成功报告 `Applied HostIntegration`，输入无效返回 64，前置或受控冲突返回 2。共享编排分阶段提交，后续失败不代表前序步骤未写入，也不是全链事务；已有恢复/授权暂存材料先拒绝接入，需要人工审查。授权候选写入前复用隔离模块编译，只在临时投影中替换 Contributor；编译失败或取消时不提交授权文件，编译期间人工漂移仍由提交复核拒绝。代表性应用的完整运行验收见本地双库入口；当前应用仍须执行自己的 Migrator、实际权限及跨租户拒绝验证，不得用命令退出 0 替代。

`TenantRequired` Schema 的生成权限使用 `AuthorizationScope.Tenant`；`HostOnly`、`Global` 保留 `Host` 权限范围，全局数据访问不自动授予租户权限。升级前已接入的 Host 授权块与新租户片段不一致时会保持原文并拒绝自动改写，应先人工审查作用域并完成实际授权验收。

命名连接环境变量还支持默认配置提供程序的 `MYSQLCONNSTR_`、`SQLCONNSTR_`、`SQLAZURECONNSTR_`、`CUSTOMCONNSTR_` 前缀，不区分大小写并规范化名称中的 `__`。其值优先于环境 JSON、Development User Secrets 和基础 JSON；占位值不会恢复低优先级凭据。同一路径存在多个环境别名时，任一占位值都保持拒绝放行，不依赖宿主枚举顺序。非空 `Database:ConnectionString` 仍优先于命名连接，自动产生的 `ProviderName` 元数据不改变 `Database:Provider`；配置存在不证明连接字符串语法、地址或数据库可用。

SDK 探测在进程启动后对退出与标准输出/错误读取设置 30 秒等待上限；即使已有部分输出，也不会延长等待。超时取消管道读取，终止仍存活的本次探测进程树并等待退出，再返回固定脱敏错误码 `code_generation.sdk.probe_timeout`；清理耗时另计，不表示整条 diagnose 命令保证 30 秒内结束。父进程已退出但子进程仍持有管道时，也会取消读取；此路径不保证回收已脱离父进程的子进程。通过 CLI 调用接口传入的取消令牌优先于超时，清理后保留调用方的取消结果与令牌，不会转为 SDK 不可用。上述边界由真实受控进程回归验证，不等同于终端信号处理。

模块配置声明提示（`DIAG_MODULES_*`）只读取所选 API 的基础配置，支持扁平键、嵌套键与大小写变体。有效非空 Preset 或 Enabled 子键可产生 `DIAG_MODULES_OK`；空集合覆盖 Preset 后不会保留旧提示，而空父节点不会清除此前声明的 Enabled 子键。保留原有嵌套字段类型检查；该提示不验证模块名称、预设成员或依赖闭包，也不代表模块绑定、环境覆盖或宿主启动已通过。

独立应用的 `DIAG_MODULE_CLOSURE_*` 另外核对冻结预设中的模块项目与 Composition 引用，支持相对路径中的反斜杠；引用比较在 Windows 忽略大小写，在 Linux 按精确大小写区分。Linux 上目录或文件名大小写不匹配会返回 `DIAG_MODULE_DEPENDENCY_MISSING`，即使另一个大小写不同的项目文件实际存在，也不能证明所选模块已接入。这仍是静态项目引用检查，不替代 MSBuild 条件求值、依赖图解析、模块服务注册或宿主启动验收。

模块与 Composition 接入编译失败时，仍保留已有编译诊断的路径替换、去重及数量上限。若底层构建没有可公开的编译诊断，CLI 在固定失败说明后附加数字 `构建进程退出码`，不回显原始 SDK/进程输出；CLI 本身仍返回 2。退出码帮助定位失败来源，不说明具体根因，也不会触发自动重试或绕过候选编译。独立打包应用用缺失 SDK 对照真实构建退出码，验证两条接入命令在编译失败时保留应用源码与人工文件，并在验收后恢复 SDK 配置。

## 第六步：显式接入业务迁移

默认 Migrator 只执行冻结预设的框架迁移，不会自动采纳根目录的业务草稿。此例只采纳 `acme_catalog_product` 的成对建表脚本；先核对 Schema 的 owner/module/entity、六列、UUID v7 的物理类型及 TenantId/Id 索引。SQL Server 将建表和索引分别按结构探测收敛，MySQL 将表与索引放在同一 CREATE 的原子 DDL 中；`IF NOT EXISTS` 不负责修复任意已有错误表。

下面的 PowerShell 命令先检查两个源和目标，再按原字节复制到应用自有 Migrator，不能放进受管框架：

```powershell
$migrationCopies = @{
  'templates/migrations/SqlServer/CreateProduct.sql.template' = 'src/Demo.Host.Migrator/Migrations/SqlServer/001_CreateProduct.sql'
  'templates/migrations/MySql/CreateProduct.sql.template' = 'src/Demo.Host.Migrator/Migrations/MySql/001_CreateProduct.sql'
}
foreach ($migrationCopy in $migrationCopies.GetEnumerator()) {
  if (!(Test-Path -LiteralPath $migrationCopy.Key -PathType Leaf)) { throw "缺少迁移源：$($migrationCopy.Key)" }
  if (Test-Path -LiteralPath $migrationCopy.Value) { throw "迁移目标已存在，先审查：$($migrationCopy.Value)" }
}
foreach ($migrationCopy in $migrationCopies.GetEnumerator()) {
  New-Item -ItemType Directory -Path (Split-Path -Parent $migrationCopy.Value) -Force | Out-Null
  Copy-Item -LiteralPath $migrationCopy.Key -Destination $migrationCopy.Value
}
```

在 `src/Demo.Host.Migrator/Demo.Host.Migrator.csproj` 的 `</Project>` 前追加一个 ItemGroup，使用固定资源名，不扫描程序集：

```xml
  <ItemGroup>
    <EmbeddedResource Include="Migrations/SqlServer/001_CreateProduct.sql" LogicalName="acme.catalog.Migrations.SqlServer.001_CreateProduct.sql" />
    <EmbeddedResource Include="Migrations/MySql/001_CreateProduct.sql" LogicalName="acme.catalog.Migrations.MySql.001_CreateProduct.sql" />
  </ItemGroup>
```

同目录保存 `ApplicationMigrationRunner.cs`：

```csharp
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using Microsoft.Extensions.Options;

namespace Demo.Host.Migrator;

/// <summary>先执行冻结框架迁移，再执行应用明确登记的业务脚本。</summary>
/// <remarks>只供应用 Migrator 使用；失败或取消阻止后续播种，已记账脚本保持固定名称和内容。</remarks>
internal sealed class ApplicationMigrationRunner(
    DbUpMigrationRunner frameworkRunner,
    IOptions<DatabaseOptions> databaseOptions,
    string sqlServerScript,
    string mySqlScript) : IDatabaseMigrationRunner
{
    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlServerScript);
        ArgumentException.ThrowIfNullOrWhiteSpace(mySqlScript);
        cancellationToken.ThrowIfCancellationRequested();
        var framework = await frameworkRunner.MigrateAsync(cancellationToken);
        if (!framework.Successful)
            throw new InvalidOperationException("框架迁移失败，不能执行应用迁移。");

        // 框架阶段完成后再次检查取消，不能进入业务阶段或后续播种。
        cancellationToken.ThrowIfCancellationRequested();
        var options = databaseOptions.Value;
        var builder = options.Provider switch
        {
            DatabaseProvider.SqlServer => DeployChanges.To.SqlDatabase(options.ConnectionString),
            DatabaseProvider.MySql => DeployChanges.To.MySqlDatabase(
                MySqlConnectionStringPolicy.Create(options.ConnectionString, options.MySqlGuidStorageMode, true)),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Provider))
        };
        var script = options.Provider == DatabaseProvider.SqlServer ? sqlServerScript : mySqlScript;
        var application = builder
            .WithScript($"acme.catalog.Migrations.{options.Provider}.001_CreateProduct.sql", script)
            .WithExecutionTimeout(TimeSpan.FromSeconds(options.CommandTimeoutSeconds))
            .LogToConsole()
            .Build()
            .PerformUpgrade();
        if (!application.Successful)
            throw new InvalidOperationException("应用迁移失败，不能继续播种。", application.Error);

        // DbUp 同步执行不能中途取消；完成后取消阻止播种，不撤销已提交和记账的 DDL。
        cancellationToken.ThrowIfCancellationRequested();
        var count = application.Scripts.Count();
        Console.WriteLine(FormattableString.Invariant(
            $"FULLNET_APPLICATION_MIGRATIONS {{\"frameworkScripts\":{framework.ExecutedScriptCount},\"applicationScripts\":{count}}}"));
        return new MigrationResult(true, checked(framework.ExecutedScriptCount + count));
    }

    internal static string ReadScript(string name)
    {
        using var stream = typeof(ApplicationMigrationRunner).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("缺少显式登记的应用迁移资源。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

在 Migrator 的 `Program.cs` 添加 `using Microsoft.Extensions.DependencyInjection;`，再在既有 `AddApplicationModules(..., FullNetHostProfile.Migrator);` **之后、RunAsync 之前**插入以下注册；保留 API/Worker 角色分离：

```csharp
builder.Services.AddSingleton<Full.NET.Migrations.DbUp.DbUpMigrationRunner>();
builder.Services.AddSingleton<Full.NET.Migrations.DbUp.IDatabaseMigrationRunner>(services =>
    new Demo.Host.Migrator.ApplicationMigrationRunner(
        services.GetRequiredService<Full.NET.Migrations.DbUp.DbUpMigrationRunner>(),
        services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Full.NET.Data.Abstractions.DatabaseOptions>>(),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadScript("acme.catalog.Migrations.SqlServer.001_CreateProduct.sql"),
        Demo.Host.Migrator.ApplicationMigrationRunner.ReadScript("acme.catalog.Migrations.MySql.001_CreateProduct.sql")));
```

先编译：

```bash
dotnet build src/Demo.Host.Migrator/Demo.Host.Migrator.csproj -c Release
```

### 仅在可销毁空库实走

把测试连接通过当前进程的 `ConnectionStrings__app` 提供，确认指向本次自有可销毁空库；不要写入教程、Git 或日志。该冻结预设包含 009 UUID Contract 与 011 Naming Contract，默认维护门禁关闭；仅配置连接会先被 Identity 签名选项阻断，开发临时密钥启用后仍会被维护门禁阻断，不能把这些退出 1 算作迁移通过。

将下面内容保存为应用根 `run-local-tutorial-migration.ps1`。它只为这次空库演练给 Migrator 提供进程级配置，结束后恢复原值，不将维护批准或临时密钥配置写进应用、API 或 Worker。维护标识与布尔值只描述本次隔离验收，不代表生产备份/维护窗口已验证；有数据的环境必须执行自己的备份恢复、停写和迁移批准流程。

```powershell
param(
  [ValidateSet('SqlServer', 'MySql')][string]$Provider = 'SqlServer',
  [ValidateSet('none', 'baseline', 'development')][string]$Seed = 'none'
)
$localConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__app', 'Process')
if ([string]::IsNullOrWhiteSpace($localConnection)) { throw '先通过进程环境提供本次可销毁空库连接。' }
$settings = @{
  DOTNET_ENVIRONMENT = 'Development'
  Database__Provider = $Provider
  Database__ConnectionString = $localConnection
  Database__MySqlGuidStorageMode = 'Binary16'
  Identity__AllowDevelopmentEphemeralSigningKey = 'true'
  FullNet__FrameworkManifest__ContentRoot = '.'
  UuidBinaryContract__MaintenanceMode = 'true'
  UuidBinaryContract__BackupVerified = 'true'
  UuidBinaryContract__LegacyWritersStopped = 'true'
  UuidBinaryContract__DestructiveDdlApprovalId = 'tutorial-disposable-009'
  PreV1NamingContract__MaintenanceMode = 'true'
  PreV1NamingContract__BackupVerified = 'true'
  PreV1NamingContract__LegacyWritersStopped = 'true'
  PreV1NamingContract__LegacyOutboxDrained = 'true'
  PreV1NamingContract__DestructiveDdlApprovalId = 'tutorial-disposable-011'
}
$previous = @{}
foreach ($setting in $settings.GetEnumerator()) {
  $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key, 'Process')
}
try {
  foreach ($setting in $settings.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value, 'Process')
  }
  $migratorArgs = @('run', '--project', 'src/Demo.Host.Migrator', '-c', 'Release', '--no-build')
  if ($Seed -ne 'none') { $migratorArgs += @('--', '--seed', $Seed) }
  & dotnet @migratorArgs
  if ($LASTEXITCODE -ne 0) { throw '迁移或播种失败，保留结果并定位；不要继续启动业务。' }
} finally {
  foreach ($setting in $previous.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value, 'Process')
  }
}
```

在应用根执行 `./run-local-tutorial-migration.ps1 -Provider SqlServer`，再重复同一命令。首次结果标记应为 frameworkScripts 大于 0、applicationScripts 为 1，重复两项均为 0；实际表还须核对六列、主键与 TenantId/Id 索引。MySQL 的独立空库使用 MySql 参数；上面的 Provider 只覆盖本次迁移进程，不修改冻结档案，正式应用各宿主配置仍须与其声明的 Provider 保持一致。

仅零脚本重复不证明恢复：本次验收还会在自有临时库撤销该业务脚本的一条记账，SQL Server 保留表/样本行并移除未完成的索引，MySQL 保留完整原子 DDL；再迁移应补齐或保持结构、只记账一次且保留样本。恢复操作只供隔离验收，不在部署或本地运行脚本中自动执行。此步默认不播种，后续账号/页面演练须通过受控配置提供 `Identity__Bootstrap__Username` 与满足强密码规则的 `Identity__Bootstrap__Password`，再显式选择 `-Seed development`；Production 仍只允许 Baseline，不能使用上述本地维护配置。

2026-10-05 已直接采纳本文代码块，在保留的 `0957ee62` 独立应用完成此步。两份业务 SQL 与草稿原字节一致；再次采纳按预期退出 1、文件保持，Migrator Release 编译 0 警告/0 错误。Windows x64、Node 24.12.0、.NET SDK 10.0.401、运行时 10.0.12、`DOTNET_PROCESSOR_COUNT=2`，串行使用 SQL Server 2022 CU14 与 MySQL 8.4 自有临时容器；完整双库实走退出 0，共 26 次迁移/结构/恢复进程核对，两个不存在数据库的失败路径各按预期退出 1，其余退出 0。首次各执行框架 99/业务 1，重复各 0/0；六列与两个索引、空业务表及零管理员行符合预期。撤销一条业务记账后，SQL Server 补回缺失索引、MySQL 保持完整原子 DDL，各只执行业务 1；样本名称与版本 7 保持，恢复后复跑均 0/0。成功和失败路径的 15 项进程配置均恢复原值；失败未改写原数据库。采纳后的应用源码、配置、草稿和全部受管框架摘要保持，仅清理本次自有容器。总耗时 156.387 秒（SQL Server 65.692 秒、MySQL 87.677 秒，含容器启动/清理，不含采纳、编译和先前失败），原始结果保留 `.tmp/f02-tutorial-migration-0957ee62-run3/`，采纳材料保留 `.tmp/f02-tutorial-migration-0957ee62/`。

失败事实另行保留：未提供开发签名时宿主 Options 校验失败；只启用开发临时密钥而缺维护配置时，两库均在 009 门禁退出 1、已记账 8 条框架脚本，业务表与管理员行仍为零，不能当作空库迁移完成。首轮结构探针被 SQL Server 系统元数据排序规则冲突阻断，第二轮被 MySQL DISTINCT 查询的排序列限制阻断；修正验收查询后才取得上述完整新结果，没有修改业务 SQL 或数据库默认配置。本步未执行 Development 播种，未启动 API 监听、Worker 或浏览器，未将静态 OpenAPI 与运行 API 比较；完整教程、诊断覆盖及 F02 整项仍待收口。

## 第七步：开发播种与启动 API

本步仍只使用本次自有可销毁数据库。通过进程环境提供 `Identity__Bootstrap__Username` 与 `Identity__Bootstrap__Password`，密码满足 Identity 强密码规则；不要复制固定演示密码或打印登录响应。应用根显式执行以下命令，Development 会先执行 Baseline，再执行自己的 Overlay；API/Worker 不播种：

```powershell
./run-local-tutorial-migration.ps1 -Provider SqlServer -Seed development
./run-local-tutorial-migration.ps1 -Provider SqlServer -Seed development
```

MySQL 演练将 Provider 改为 MySql。重复播种必须核对实际账号和 local 租户，而非只查看退出码；不能覆盖用户改密。若播种失败，保留固定机器码并定位，不继续当作初始化成功。

```bash
dotnet build src/Demo.Host.Api/Demo.Host.Api.csproj -c Release -v quiet
```

应用不会自动读取受管框架的 Development 配置。以下内容保存为应用根 `start-local-tutorial-api.ps1`；先通过当前进程的 `ConnectionStrings__app` 与 `Cache__RedisConnectionString` 提供本次数据库和 Redis 连接。它将配置仅传给新 API 进程，并立即恢复调用进程的原值；不向 API 提供迁移维护标识或 Bootstrap 密码。示例通过普通 HTTP 开发地址运行，不能用作生产配置：

```powershell
param(
  [ValidateSet('SqlServer', 'MySql')][string]$Provider = 'SqlServer',
  [ValidateRange(1024, 65535)][int]$Port = 25182,
  [string]$VueOrigin = 'http://localhost:25183',
  [Parameter(Mandatory)][string]$StdoutPath,
  [Parameter(Mandatory)][string]$StderrPath
)
$localConnection = [Environment]::GetEnvironmentVariable('ConnectionStrings__app', 'Process')
$localRedis = [Environment]::GetEnvironmentVariable('Cache__RedisConnectionString', 'Process')
if ([string]::IsNullOrWhiteSpace($localConnection) -or [string]::IsNullOrWhiteSpace($localRedis)) {
  throw '先通过进程环境提供本次测试数据库和 Redis 连接。'
}
$apiDll = (Resolve-Path -LiteralPath 'src/Demo.Host.Api/bin/Release/net10.0/Demo.Host.Api.dll').Path
if ((Test-Path -LiteralPath $StdoutPath) -or (Test-Path -LiteralPath $StderrPath)) {
  throw '日志目标已存在，先审查；不要覆盖已有验收材料。'
}
$settings = @{
  DOTNET_ENVIRONMENT = 'Development'
  ASPNETCORE_ENVIRONMENT = 'Development'
  Database__Provider = $Provider
  Database__ConnectionString = $localConnection
  Database__MySqlGuidStorageMode = 'Binary16'
  Identity__AllowDevelopmentEphemeralSigningKey = 'true'
  Identity__RequireSecureCookies = 'false'
  Identity__AllowedOrigins__0 = 'http://localhost'
  Identity__AllowedOrigins__1 = 'http://127.0.0.1'
  Identity__AllowedOrigins__2 = $VueOrigin
  Identity__LoginRateLimitPermitLimitPerMinute = '20'
  Identity__Bootstrap__Password = $null
  UuidBinaryContract__MaintenanceMode = $null
  UuidBinaryContract__BackupVerified = $null
  UuidBinaryContract__LegacyWritersStopped = $null
  UuidBinaryContract__DestructiveDdlApprovalId = $null
  PreV1NamingContract__MaintenanceMode = $null
  PreV1NamingContract__BackupVerified = $null
  PreV1NamingContract__LegacyWritersStopped = $null
  PreV1NamingContract__LegacyOutboxDrained = $null
  PreV1NamingContract__DestructiveDdlApprovalId = $null
  Tenancy__HostDomains__0 = 'localhost'
  Tenancy__HostDomains__1 = '127.0.0.1'
  Cache__RedisConnectionString = $localRedis
  Realtime__RedisBackplaneConnectionString = $localRedis
  Realtime__AllowSharedRedisInDevelopment = 'true'
  Kestrel__Endpoints__Http__Url = "http://127.0.0.1:$Port"
  FullNet__FrameworkManifest__ContentRoot = '.'
}
$previous = @{}
foreach ($setting in $settings.GetEnumerator()) {
  $previous[$setting.Key] = [Environment]::GetEnvironmentVariable($setting.Key, 'Process')
}
try {
  foreach ($setting in $settings.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value, 'Process')
  }
  $apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList ('"' + $apiDll + '"') -WorkingDirectory (Get-Location).Path -WindowStyle Hidden -PassThru -RedirectStandardOutput $StdoutPath -RedirectStandardError $StderrPath
} finally {
  foreach ($setting in $previous.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($setting.Key, $setting.Value, 'Process')
  }
}
$apiProcess
```

确认 25182 与 25183 端口没有被占用后，调用 `$tutorialApi = ./start-local-tutorial-api.ps1 -Provider SqlServer -StdoutPath "$PWD/tutorial-api.stdout.log" -StderrPath "$PWD/tutorial-api.stderr.log"` 并保存返回的进程对象；MySQL 将 Provider 改为 MySql。等待该 API 的 `/health/live`、`/health/ready` 实际返回 200，再核对 `/openapi/v1.json` 的五条商品操作与生成契约；没有 readiness 或真实登录，不能把进程已创建算作启动验收。演练结束仅停止自己保存的 API 进程，并检查端口释放；运行日志保存前应核对脱敏，不记录凭据。

Vue 使用同一 API 代理和精确 Origin，从应用根另开终端运行：

```powershell
$env:VITE_API_PROXY_TARGET = 'http://127.0.0.1:25182'
$env:VITE_STRICT_CSP = '1'
node ./ui/admin/node_modules/vite/bin/vite.js --host localhost --port 25183 --strictPort --logLevel error
```

浏览器从 Vue 登录页进入、选择 local 租户并通过商品菜单进入页面；租户标签和首页跳转均已完成后再点击菜单。超级管理员不能代替普通账号的逐操作权限验收；读取、无权限、创建、更新和删除账号分别验证菜单、直达路由、按钮与服务端拒绝，再核对持久化结果和跨租户不泄露。真实账号与浏览器结果单独记录，不能从静态导航或构建结果推断。

2026-10-05 已在同一 `0957ee62` 应用直接执行第七步的开发播种与启动脚本，API Release 构建退出 0、0 警告/0 错误，35.70 秒。Windows x64、Node 24.12.0、.NET SDK 10.0.401/运行时 10.0.12、DOTNET_PROCESSOR_COUNT=2，串行 SQL Server 2022 CU14、MySQL 8.4 与 Redis 7.4：完整双库运行退出 0，307.201 秒（SQL Server 119.143 秒、MySQL 151.797 秒，合计另含端口释放与文件摘要核对；不含 API 编译及先前失败）。两库 Development 首次播种包含 Baseline 与 Overlay，重复仍只有一名引导管理员及一个 local 租户；改用不同 Bootstrap 密码复跑后，原密码登录 200、新配置密码登录 401，证明未重置已有管理员密码。API 调用进程的 28 项配置恢复原值；缺连接、已有日志两项独立拒绝均退出 1，原配置与日志保持。live/ready 均 200，运行 OpenAPI 的五操作、五参数形态、十认证错误形态、三请求及五响应形态与生成子集一致。

每库五类普通账号均通过 Vue 登录和租户切换、菜单/直达路由及精确按钮权限；创建、更新、删除在真实浏览器提交后返回 201/200/200并由管理员 API 核对持久化，删除确认的键盘取消不发请求、确认恰好发一次。生成列表及操作弹窗的 axe WCAG 2/2.1 A/AA 自动审计零违规，不等于全站或辅助技术人工验收。匿名和 Host 管理员的十条业务拒绝、真实租户 CRUD 的 11 条业务请求及两次版本冲突通过；跨租户 20 条业务请求中六次外租户访问返回未找到，两个租户各自数据保持并清理。十份浏览器报告 completed=true，源代码、配置、人工扩展和框架摘要保持；只清理本次 API、Vue、浏览器及容器，端口释放通过。原始结果 `.tmp/f02-tutorial-runtime-0957ee62-run2/`。

首轮失败保留 `.tmp/f02-tutorial-runtime-0957ee62/`：API 已监听且 PowerShell 调用进程已退出，验证器仍等待后台进程继承的输出管道关闭；停止本次 API 后调用才返回，后续 readiness 失败，未计为通过。验证器改用独立文件接收启动调用输出后，才取得上述完整新结果，没有修改 API 或绕过健康检查。结合前四步、后端、Vue 和迁移的同一冻结应用分段证据，关闭 F02 的教程子项；摘要 `.tmp/f02-tutorial-composite-0957ee62.json`。各阶段使用独立临时数据库，不是单次从空目录连续全量重跑，也不能累加局部计时作为冷启动或开发耗时承诺。MySQL 实走只覆盖同一 SQL Server 档案应用的迁移/API进程，不认证新 MySQL 模板档案。本轮未启动此教程应用 Worker、未发布其 Native AOT 或验证容量；完整配置诊断仍待收口，F02 整项保持未关闭。

## 验证

新创建应用在签名、数据库及框架维护前提已满足后，从应用根运行 `dotnet run --project src/<name>.Host.Migrator -- --seed baseline`，迁移成功后才执行显式播种；省略 `--seed` 只迁移。仅本地开发环境显式选择 Development 后才能使用 `--seed development`，Production仍只允许Baseline；此 Demo 的隔离空库演练使用第六步脚本。API、Worker和Migrator消费同一应用Composition，分别装配各自Profile；Migrator只注册模块的迁移/播种入口，不能装入API Profile。Worker编译随应用分发的框架后台处理管线，默认健康检查端口与API分开；其运行时和Native AOT验收须单独执行。默认Runner只运行冻结预设的框架脚本；第六步的应用自有包装器显式追加业务脚本。生成业务SQL草案须完成编号、所有权、恢复与双库评审后显式接入，不能放进受管框架目录。旧应用的源码升级不会自动创建该应用拥有的宿主，需按新模板显式采用；默认结构校验兼容旧应用，创建发布前则强制要求同名Worker、Migrator与一致配置。

内容根声明 `fullnet-app.json` 时，Migrator 要求 `framework-manifest.json` 包含有效的预设迁移清单；文件缺失、清单不完整或 `unscoped` 会在解析数据库连接前停止，防止静默扩大为全部框架迁移。未声明应用的框架工作区保留原有非限定兼容行为。该检查依赖内容根中的应用声明，不替代发布目录的配置核验。

显式限定清单的脚本名必须唯一，并精确对应当前框架程序集中的 SQL Server/MySQL 成对资源；未知、截短、大小写漂移或带路径名称会在连接解析前拒绝。此校验允许有效子集，不验证 SQL 摘要、预设归属或数据库当前结构，也不是业务草案注册入口。

应用可以在自有 Migrator 中显式替换 `IDatabaseMigrationRunner`：包装框架 `DbUpMigrationRunner`，先等待框架迁移成功，再运行已编号并经双库评审的业务脚本，返回合计执行数；任一阶段失败或取消时，既有工作流不得继续播种。业务脚本只嵌入应用 Migrator，使用含 owner/module 的稳定 journal 名称，不能进入 API、模块项目或受管框架迁移目录。默认模板不会自动采纳业务草稿。

独立应用真实栈验收增加这一显式路径：先用应用包内 CLI 生成 `acme_catalog_product` 双库草稿，再将原文字节采纳为应用自有 `001_CreateProduct.sql` 并登记固定资源。首次运行要求业务脚本执行数为 1，重复迁移要求框架与业务均为 0；每个提供程序的采纳摘要和执行结果保存在 `.tmp/template-real-stack/<provider>/application-migration-*.json`，双库实际结果以对应提交的 Actions 为准。这一阶段验证迁移执行与记账，尚不证明生成业务 HTTP CRUD、跨租户拒绝、页面或完整 F02 验收通过。

真实栈还复用模块、Composition 与授权贡献者的 CLI 接线，将生成业务端点装入 API。对 `catalog/products` 的列表、详情、新增、更新、硬删除逐一发送匿名请求和真实 Host 引导管理员请求，分别要求 401 与 403，以及对应 ProblemDetails 机器码；404、重定向、500 或 Host 认证失效均不能计为权限拒绝成功。请求证据写入各提供程序目录的 `application-crud-http-denial.json`，不记录 Bearer 令牌，失败时保留已执行请求。本阶段只覆盖匿名和 Host 对租户业务的拒绝，不证明有权限租户 CRUD、无权限租户或跨租户数据隔离，实际结果仍以对应提交双库 Actions 为准。

租户 CRUD 阶段复用真实可用租户目录，精确选择 Development `local`，通过上下文切换 API 取得新令牌；业务请求不提供 TenantId，不直接写库或构造 Claims。验收新增、按 ID 与列表读取、版本更新，以及旧版本更新/删除返回 409 后再次读取确认内容与版本未变，最后硬删除并确认 404 与列表移除；响应同时核对 UUID v7、服务端 TenantId、字符串 Version 和标准机器码。报告为各提供程序目录的 `application-crud-tenant-http.json`，省略签发响应正文并脱敏令牌。该主体是 Host 管理员经授权切入租户上下文；实际结果由对应提交的双库 Actions 确认，不能据此证明普通租户账号精确权限、无权限租户或跨租户数据隔离。

双租户隔离阶段仅在内存续接最新会话，先经上下文 API 返回 Host，再通过租户开通 API 创建本次独立数据库中的第二租户。双方各创建一条产品，双向对对方 ID 的读取、更新、删除均要求 `catalog.products.not_found`／404，列表只含当前租户记录；切回后核对双方 Name、TenantId、Version 未变，最后分别删除自有记录并核验 404 与空列表。每次切换只使用新签发令牌，业务请求不传 TenantId。报告为 `application-crud-tenant-isolation.json`，不写入续接令牌或签发正文；双库实际行为以对应提交 Actions 为准。此阶段验证获授权 Host Actor 在不同有效租户上下文中的数据过滤，普通账号权限仍由独立阶段验收。

普通账号只读权限阶段通过公开 API 创建自定义角色和普通 Host 账号，分配租户上下文页面闭包 `tenancy.tenants.read`／`tenancy.tenants.switch`，产品仅授予 `catalog.products.read`。真实登录后携带服务端 CSRF Cookie 完成首次改密，核对 `/api/v1/me` 的非超级管理员标记及 Host/租户精确权限，再读取管理员创建的产品。列表和按 ID 读取应成功，创建、更新、删除应返回标准 403／`authorization.permission_denied`；管理员再次读取和列表核对原行未变且无新增行，然后清理产品并返回 Host。`application-crud-read-permission.json` 不记录密码、Cookie、签发正文或令牌；本地 Node 测试仅验证验收门禁，实际账号、会话与双库结果须检查对应提交 Actions。该阶段只覆盖普通 Host Actor 切入租户后的只读精确权限，完全无产品权限账号和各写权限独立正向仍待验收。

1. 运行迁移并启动 Host.Api
2. 使用对应租户与精确权限的账号登录管理端，访问应用实际接入的生成页面
3. 执行租户 CRUD、无权限及跨租户拒绝用例，再验证二次生成和人工修改保护；F02 完整验收尚未关闭

SQL Server 租户草案在建表后独立探测并创建租户聚集索引，使“表已创建、索引未完成、迁移未记账”的重跑可以补齐索引。MySQL 草案的索引仍在单条原子建表语句内，不修复外部删除索引的状态。两份草案均不修复任意错误的既有表结构，正式迁移仍需双库恢复评审与显式接入。

## 本地真实链路验收

在原框架仓库根目录执行下面的用例，而不是在新应用目录执行。先按锁文件安装仓库依赖，并确认 .NET 10 SDK、Docker Linux Engine 与 Playwright Chromium 可用；测试会创建临时独立应用和专用数据库容器。

```powershell
$env:FULLNET_RUN_TEMPLATE_REAL_STACK = '1'
node --throw-deprecation --test --test-concurrency=1 tests/templates/created-app-real-stack.test.mjs
```

该入口依次验证 SQL Server 与 MySQL 的迁移、API/Worker、OpenAPI 客户端、Vue 浏览器 CRUD、普通账号精确权限、双租户拒绝、人工内容保护及已有业务数据升级恢复。报告位于 `.tmp/template-real-stack/<provider>/`；先核对本次源码 SHA、报告时间与终态，不能读取旧报告补齐未执行阶段。关闭该子项要求两库用例均通过且零跳过；OOM、超时、失败或只通过其中一库都不能计为双库成功。它不覆盖任意业务 Schema、应用自有 Native AOT 或容量实测。

验收遵循[开发质量 §11](../../rules/development-quality.md#11-测试与验证)：规定范围的本地实际通过即可验收，GitHub Actions 继续提供回归证据。文中各历史增量的 Actions 记录保留原事实，不构成当前本地验收的额外前置。

## 故障排查

| 机器码 | 含义 | 处理 |
| --- | --- | --- |
| `DIAG_SDK_MISSING` | SDK 命令不可用或目标工作区未能解析 SDK | 安装 .NET 10 SDK，核对目标工作区的 `global.json` |
| `DIAG_SDK_INCOMPATIBLE` | 所选 SDK 不符合当前 10.0.100+ 的 .NET 10.0 基线，或版本输出格式无效 | 核对目标工作区及父目录的 `global.json`；升级选择的 SDK |
| `DIAG_DATABASE_PROVIDER_INVALID` | 最终生效的数据库 Provider 名称或数字值无效，无法通过宿主枚举绑定或校验 | 核对 `Database:Provider`，使用 `SqlServer` 或 `MySql` |
| `DIAG_DATABASE_TIMEOUT_INVALID` | 显式命令超时不能绑定为正整数（含零、负值、空值和溢出） | 设置 `Database:CommandTimeoutSeconds` 为正整数秒数，缺省为 30 秒 |
| `DIAG_DATABASE_GUID_STORAGE_INVALID` | Guid 模式值无效、Production 未显式配置，或生产 MySQL 使用旧字符模式 | 显式设置 `Database:MySqlGuidStorageMode`；生产 MySQL 必须为 `Binary16` |
| `DIAG_APPSETTINGS_INVALID` | JSON 语法、结构或字段类型无效 | 修正配置类型，诊断不输出字段值 |
| `DIAG_WORKSPACE_INCOMPLETE` | 目录结构不完整 | 确认在应用根目录运行 |
| `DIAG_MODULES_MISSING` | 未配置模块预设 | 添加 `FullNet:Modules:Preset` |
| `DIAG_CONNECTION_PLACEHOLDER` | 开发环境缺连接 | user-secrets 或环境变量 |
| `DIAG_USER_SECRETS_INVALID` | Development 的 API User Secrets 文件不可读取、JSON 无效或配置键重复 | 修复本机秘密文件；诊断不输出其内容 |
| `DIAG_SECRETS_PLACEHOLDER` | 已配置的 Redis/SM2 秘密键最终仍为空或占位符 | Development 可用 User Secrets 或环境变量覆盖；Production 使用部署密钥或环境变量，勿提交仓库 |

`diagnose` 检查数据库连接及这三个常见秘密键时，按环境变量、Development User Secrets、所选环境 JSON、`appsettings.json` 的顺序取值；Production 不读取 User Secrets。环境 JSON 取自所选 API 基础配置所在目录的 `appsettings.Development.json` 或 `appsettings.Production.json`，文件可缺省；只读所选环境，文件无效或重复配置键即报 `DIAG_APPSETTINGS_INVALID`，有效环境变量也不能掩盖文件错误。连接名 `Database:ConnectionName` 与 Provider 值 `Database:Provider` 使用相同覆盖顺序。Provider 检查对照宿主枚举绑定：名称不区分大小写，数字 `0/1` 分别对应 SQL Server/MySQL；缺省或 `null` 保留 SQL Server 默认值，显式空字符串和未定义值报错。该检查覆盖标量值和空集合叶键，不替代完整 Options 绑定（含非空集合结构及其他启动配置）的真实启动验收。命令超时和 Guid 存储模式也采用相同覆盖顺序：超时缺省为 30 秒，显式 `null` 或空对象按运行时绑定为 0 并拒绝，合法正整数及运行时支持的十六进制前缀保留兼容。Guid 模式允许名称或数字 `0/1`（`LegacyChar36/Binary16`）；Production 两库均须显式填写非 null 值，MySQL 必须使用 `Binary16`。Development 缺省 Guid 模式保留旧模式供迁移窗口使用，诊断不会修改配置或迁移数据。冻结应用预设检查仍核对基础配置。环境配置键按运行时语义不区分大小写；较高优先级的占位值或显式 `null` 不会被较低优先级的有效值掩盖。未出现的可选秘密键不算占位符；`DIAG_SECRETS_OK` 不代表全部运行时依赖已配置，仍需执行生成应用和真实栈验收。诊断只输出机器码、数量与固定配置路径说明，不回显连接名或秘密值；连接提示中的 `<name>` 取自 `Database:ConnectionName`。

## 实走记录（2026-09-17，企业预设收口）

在仓库根执行：

```bash
dotnet build src/Tools/Full.NET.CodeGeneration.Cli -c Release
dotnet exec src/Tools/Full.NET.CodeGeneration.Cli/bin/Release/net10.0/Full.NET.CodeGeneration.Cli.dll diagnose --workspace .
```

结果：`DIAG_WORKSPACE_OK`、`DIAG_SDK_OK`；连接串与 `FullNet:Modules` 为占位 warn（仓库根非应用模板，符合预期）。Enterprise Request 样例仅 Schema 测试通过（F09 骨架）。

无产品权限普通账号阶段复用上述公开账号、角色、登录与CSRF首次改密链，使用独立角色和账号，只授予租户上下文页面闭包 `tenancy.tenants.read`／`tenancy.tenants.switch`，不授予任何 `catalog.products.*` 权限。Host与租户 `/api/v1/me` 均核对这两项精确集合、非超级管理员及改密完成；管理员预建产品后，该账号对列表、按ID读取、创建、更新、删除五个入口都应返回403／`authorization.permission_denied`。管理员再读和列表确认原行版本内容保持且没有新增，然后清理行并返回Host。各提供程序报告为 `application-crud-no-permission.json`，不保存凭据；真实账号与双库结果仍须对应SHA Actions报告确认，各写权限独立正向还须单独验收。

普通账号创建权限阶段使用独立角色与账号，权限固定为产品页面闭包 `catalog.products.read`／`catalog.products.create` 和租户上下文 `tenancy.tenants.read`／`tenancy.tenants.switch`；仍通过真实登录、CSRF首次改密及 `/api/v1/me` 精确权限核验。它可列表/读取管理员预建行并创建新行201，新行必须是不同UUID v7、当前可信TenantId、预期Name和字符串Version1；更新与删除仍须403。管理员读取原行并核对列表恰含两条完整记录，再清理两行、核对普通创建行404，返回Host后仅内存续接新令牌。报告为 `application-crud-create-permission.json`，真实创建、拒绝与清理必须以对应SHA双库Actions为准；更新和删除权限独立正向仍需后续验收。

普通账号更新权限阶段使用独立角色与账号，固定授予产品 `catalog.products.read`／`catalog.products.update` 与租户上下文read/switch闭包，真实首次改密后核对Host/Tenant精确权限与非超级管理员。创建403，读取原行后更新200，将Version字符串1推进到2并保持Id/TenantId；旧版本1更新必须409／`catalog.products.version_conflict`，立即读取确认已提交内容和版本保持，删除仍403。管理员再读和唯一行列表检查持久化及无新增，再用Version2清理，回Host仅内存续接。报告为 `application-crud-update-permission.json`，实际账号/双库更新与冲突行为须对应SHA Actions确认；删除权限独立正向仍待补齐。

普通账号删除权限阶段使用独立角色与账号，固定授予产品 `catalog.products.disable`／`catalog.products.read` 和租户上下文read/switch。当前hard.delete路由沿用既有Disable权限机器码；不新增delete权限。真实首次改密及me精确权限核验后，创建和更新403，当前Version1产品用不匹配Version2删除须409且再读保持原行；正确Version1删除200返回原行。普通账号与管理员分别核对GET404和空列表，确认实际消失，再返回Host。报告为 `application-crud-delete-permission.json`，无需管理员重复删除；固定五类权限入口仍须以对应SHA双库Actions证明，不把Node替身计为真实账号/数据库通过。

OpenAPI 接入阶段在独立应用真实 API 就绪后匿名读取 `/openapi/v1.json`，对照只读生成文件 `contracts/openapi/products.generated.openapi.json`，逐项核对五个产品操作的路径（仅规范化尾斜杠）、operationId、成功状态和 Bearer 声明。三种写请求的字段集合不能暴露Id/TenantId/创建审计；五种成功响应的字段集合须与生成契约一致，列表比较items元素。只解析文档内components/schemas引用，拒绝缺失、外部及循环引用；报告 `application-crud-openapi.json` 保存实际比较和失败部分证据，生成文件字节保持。此阶段仅证明真实文档的路由、安全声明及字段接入，完整错误元数据、数字/字符串类型细节与Vue客户端仍须后续验收，不能仅凭Node替身认定双库文档服务通过。

认证错误元数据补齐：生成 Endpoint 的每个受保护操作现在显式声明401/403 `ProducesProblem`，与生成静态契约和既有真实HTTP拒绝保持一致；不改变运行授权或返回行为。OpenAPI门禁对五操作逐项要求这两状态具有 `application/problem+json` 与可解析ProblemDetails/status，报告 `authenticationProblems: 10`。404/409等完整操作错误集、机器码扩展schema及数字/字符串类型细节仍须后续，不把新增注解算作全部契约认证。

参数契约子集：同一真实文档门禁继续比较五操作的参数名称、query/path位置、必填性、基础标量类型与format，报告 `parameterShapes: 5`。路径ID必须保持必填UUID，分页保持可选int32，不能出现额外TenantId参数、缺失或重复参数。可选查询参数允许CLR可空schema与参数顺序差异；不由此放宽路径可空。此子集不验证分页默认值/范围、请求响应字段类型或完整错误契约，真实接入仍由对应SHA双库Actions证明。

认证错误基础字段子集：五操作各401/403的ProblemDetails须声明type/title/status/detail/instance；status非空类型为integer/int32，其余为string。容忍标准字段可空或非必填、业务扩展字段，文档内字段schema引用仍须可解析；外部、缺失或循环引用拒绝。报告每状态的 `authenticationProblemFields`，不以字段存在代替类型检查，也不据此声称机器码扩展、404/409等完整错误集已认证。

ASP.NET 数字读取兼容：实际文档的分页参数及ProblemDetails.status可以声明integer|string。门禁仅在类型恰为这两项、format为int32且pattern为 `^-?(?:0|[1-9]\d*)$` 时归一为整数；可空处理沿既有边界，路径UUID不适用。缺少整数约束、任意字符串/对象或int64仍拒绝。报告同时保留 `parameterDeclarations` 和 `authenticationProblemDeclarations` 原始schema声明，失败时可核对实际生成形态；规范化结果不等于改变运行时序列化。

生成模块人工修改保护：独立应用对六个受管产物（模块注册、Contracts、SQL、Endpoint、Feature、Record）逐个追加测试注释并重新apply。每轮须退出2；注册桥在规划前失败，stdout为空且stderr含确切原因与路径；五实体产物的完整计划仅目标Conflict、其他Unchanged。所有生成文件、manifest、人工文件及宿主内容字节保持；验收通过后仅撤销本轮注释。报告列出conflictArtifacts和六轮独立CLI证据。此阶段验证覆盖保护，不替代真实业务人工扩展或Vue再生成的完整验收。

远端证据：`e056a683` 的独立生成应用双库作业（[CI 36310169977](https://github.com/yan041108/Full.NET/actions/runs/36310169977/job/108594450730)）成功367/367、零失败/跳过。两库报告均完成五操作、五参数形态、十认证错误及基础字段、三请求与五响应字段比较；原始schema确认ASP.NET数字兼容约束，生成输入保持。此证据覆盖OpenAPI已实现子集，未覆盖后续六产物保护、完整业务错误或Vue，不据此关闭F02。

六产物保护远端证据：`1b5dab04` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36310555699/job/108595520455)成功370/370、零失败/跳过。两库各六轮实际CLI冲突均退出2，包含桥的规划前诊断和五实体完整冲突计划；文件保持及测试注释清理完成，后续真实CRUD与OpenAPI子集继续通过。此证据不替代人工业务扩展及Vue完整再生成，F02仍未关闭。

应用公开操作清单：仓库客户端生成工具可用 `node scripts/openapi/generate-fullnet-client.mjs --input <应用OpenAPI文件> --manifest <应用清单文件> --output <客户端产物目录>`；清单的publicOperationIds为唯一非空操作名数组，未列出的匿名操作拒绝生成。未指定manifest仍使用工具所在仓库或应用的默认清单。此入口提供应用契约边界输入；新模板分发路径见下文，Vue接入/编译仍待验收，不要用客户端生成成功替代页面验收。

新模板将客户端生成脚本及校验器分发至 `.fullnet-tools/openapi/`，默认OpenAPI和公开操作清单位于应用 `contracts/openapi/`。在应用目录运行 `node .fullnet-tools/openapi/generate-fullnet-client.mjs --check` 检查其冻结客户端基线；需要业务输入时显式传 `--input`、`--manifest`、`--output`。这些文件来自同一固定源码包及摘要，初始化后应用拥有副本；旧应用需显式采用，框架源码升级不会自动覆盖应用工具/契约。工具可运行不等于业务客户端或Vue接入已验收。

工具分发远端证据：`43bf0718` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36315494268/job/108609299088)成功374/374、零失败/跳过。实际模板创建核对四份工具/契约副本与冻结源码字节一致；SQL Server/MySQL 的 `application-client-tools.json` 均记录应用自带工具退出0、生成产物零漂移、stderr为空。此证据验证默认客户端基线，不覆盖业务客户端接线、Vue编译或页面使用，F02仍未关闭。

业务客户端独立目录：生成业务契约时可追加 `--http-module @fullnet/client-contracts`，让操作文件从共享包导入 `HttpClient` 与 `RequestOptions` 类型；默认仍引用 `../http.js`，保持框架基线。使用 `--check` 时须带相同引用参数。业务生成目录应由应用单独拥有，保留共享包已有认证和基础操作；使用方仍须配置包解析并显式接入业务适配器。此选项仅改变类型引用，不自动注册路由或改变运行时请求、认证及租户上下文。

独立应用双库作业在服务端 OpenAPI 比较通过后，使用应用自带生成器消费商品生成契约，写入专用 `verification/ClientGeneration/generated`，生成并编译五个业务操作，再检查零漂移。TypeScript 编译通过 paths 映射应用自己的共享 `http.ts`，不改原业务契约、共享 HTTP 文件或四份官方客户端产物；报告位于 `application-crud-client/`。这是业务类型生成与共享 HTTP 契约兼容验证，不证明包解析、业务请求执行、Vue适配器、页面或生产构建；对应新SHA的远端结果需另行确认。

业务客户端远端证据：`badfad64` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36325119133/job/108636301949)成功380/380、零失败/跳过。SQL Server/MySQL 的 `application-crud-client/result.json` 均记录五操作、四文件、compiled/zeroDrift/inputsUnchanged为true；各生成、编译、check进程均退出0。此证据使用应用实际生成的硬删除契约，关闭该类型生成与编译验收的待验证项，Vue接线及其他F02缺口仍保留。

生成客户端匿名运行验收：编译后的业务操作与应用自有共享 HTTP 实现从专用 `verification/ClientGeneration/emitted` 加载，登录前通过五个生成操作调用 API。每项必须抛出401及 `identity.session_not_active`，不注入凭据、不刷新或重试；`application-crud-client/runtime.json` 只记录操作名、状态及机器码，失败时保留completed=false。此验证只覆盖匿名拒绝链路，不覆盖成功CRUD、包解析、浏览器或Vue；新SHA双库结果需独立核对。

运行验收在隔离 Worker 中观测真实 fetch 状态，每操作恰好一次响应，HTTP状态和解码后的ProblemDetails.status都必须为401。报告额外记录httpStatus；正文伪报401而HTTP返回403/500不得通过。观测不改变宿主线程fetch或生产共享HTTP实现。

匿名运行远端证据：`006ca78f` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36331246301/job/108653465437)成功385/385、零失败/跳过。两库runtime.json均completed=true，五操作的httpStatus/status均401、code均identity.session_not_active。这证明生成操作经应用共享HTTP实现抵达真实API并被拒绝，不证明允许CRUD或Vue页面。

Host拒绝增量：登录后、进入租户前，另用生成客户端执行五个操作，HTTP与正文状态均须403，code须authorization.permission_denied。Host凭据只在内存传到隔离Worker，重试关闭；host-runtime.json及错误消息脱敏，不能写入token。对应新SHA双库结果须独立核对，不能用本地测试服务器替代真实授权证明。

Host运行远端证据：`44ad7883` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36335535861/job/108665550676)成功391/391、零失败/跳过。两库host-runtime.json均completed=true、subject为host-admin，五操作实际HTTP/body403及authorization.permission_denied；未存凭据。该证据不代表成功业务请求。

租户成功列表增量：在既有租户CRUD验收移交可信会话后，生成客户端调用列表操作，要求单次HTTP200、page=1/pageSize=5，并由生成的响应解析器校验分页契约。tenant-read.json仅记录操作、状态和条数，不写凭据/响应正文；失败保持completed=false。仅覆盖列表读取，不代表生成客户端完整CRUD、非空数据隔离或Vue，真实双库结果须按新SHA核对。

列表远端证据：`32fc0330` 的[独立应用双库作业](https://github.com/yan041108/Full.NET/actions/runs/36338997482/job/108675258575)成功394/394、零失败/跳过；两库tenant-read.json均completed=true、单次HTTP200、items=0。此作业只证明空列表的生成客户端读取；同SHA主CI的Workflow Todo SQL Server测试遭deadlock失败，不将整条CI计为通过。

非空商品读取增量：独立应用在既有租户CRUD实际创建并核对商品后、更新前，以同一内存租户会话运行生成的 `catalogGetProduct`。必须单次HTTP200，由生成响应解析器接收，再匹配商品Id、TenantId、Name、Version；失败时停止后续更新/删除并保留未完成报告。`product-read.json`仅记录操作和HTTP状态，不写令牌或响应正文。这覆盖一个非空商品的生成客户端读取，不代表完整客户端CRUD或Vue页面。

独立应用 Vue 构建增量：将应用生成的四份 OpenAPI 客户端文件原样放入应用自有 `packages/client-contracts/src/application-generated/catalog-product/`，在该应用的包入口仅显式导出商品操作与模型；生成的 Vue 页面、页面模型和薄适配器原样放入应用管理端。使用 Vue-only 显式目标运行 `apply-client-route-integration`，复跑必须报告 `Unchanged`，随后在应用内执行冻结锁文件安装与 Vue 生产构建，并核对生成输入与应用文件未被构建改写。`application-crud-vue/result.json` 记录文件数、路由重复接入和构建状态。这一门禁验证独立应用的接线与编译，不等于真实浏览器交互、动态导航可达、普通账号按钮权限或页面可访问性验收；F02 仍未关闭。

独立应用导航契约增量：显式能力生成的服务端导航 Id、RouteName、ComponentKey 和所属操作目录统一使用可区分模块/资源边界的长度前缀机器码（本样例为 `m7-catalog-products`），旧版能力保留原标识。应用采纳 Vue 页面前须按目录字段检查本地白名单的组件键、路由名和路径未被占用，再登记同一键、路由名与路径；产物在应用内编译后执行白名单正反例，确认匹配项可接受、路由名偏离时失败关闭。服务端生成片段、Vue 路由与本地白名单三处必须一致。该门禁本身只验证静态与运行时导航契约；后续真实浏览器证据见下文，不能仅据导航契约关闭 F02。

真实浏览器增量：`09756949` 的[独立生成应用双库 CI](https://github.com/yan041108/Full.NET/actions/runs/37121124794)成功，模板测试 423/423、零失败/跳过。SQL Server/MySQL 各使用五种普通账号，在应用自己的 Vue 登录页进入并切换租户；无商品读取权限时菜单和直达路由失败关闭，其他账号仅显示被授予的操作按钮。创建、编辑、删除账号又分别在浏览器实际提交表单或点击删除，返回 HTTP 201/200/200，页面行随之变化；管理员 API 读回持久化结果或确认删除，并清理独立测试商品。`application-crud-browser/{read,none,create,update,delete}.json` 与相应权限报告均完成，不记录令牌。此证据覆盖该样例的浏览器交互与精确权限，未覆盖页面可访问性、应用 Worker、完整 Vue 再生成或容量实测，F02 继续保持未关闭。

生成页无障碍与删除确认增量：`ebeaa38b` 的[独立生成应用双库 CI](https://github.com/yan041108/Full.NET/actions/runs/37125818016)成功，模板测试 423/423、零失败/跳过。SQL Server/MySQL 各五份浏览器报告均 `completed: true`；商品列表及创建、编辑、删除弹窗的 axe WCAG 2/2.1 A/AA 自动审计零违规。删除按钮通过键盘 Enter 打开确认框，Escape 取消后商品仍可见且未发送删除请求；再次打开并确认后恰好发送一次删除请求，HTTP 200 且页面行消失。生成器区分硬删除与旧版停用提示，硬删除明确说明不可撤销；删除确认按钮使用满足本次审计的高对比度危险色。此证据只覆盖代表性生成 CRUD 页的自动审计与键盘/删除请求路径，不等于全站无障碍或辅助技术人工验收；应用 Worker、完整 Vue 再生成与容量实测仍待验证，F02 不据此关闭。

采纳后的 Vue 再生成保护增量：`596532d9` 的[独立生成应用双库 CI](https://github.com/yan041108/Full.NET/actions/runs/37127859802)成功，模板测试 424/424、零失败/跳过。早期 SQL 冲突检查先验证拒绝覆盖，再撤销仅由验收加入的注释，使后续再生成从干净受管源开始。两库的 `application-crud-vue/regenerate.json` 均以状态 0 报告全部 14 个生成产物 `Unchanged`；已采纳 Vue 页中的人工扩展及应用文件字节保持。随后人为改动生成源 `clients/vue/productsView.vue`，两库 `vue-conflict.json` 均以状态 2 精确报告该文件 `Conflict`，未改写应用页或其他产物；源文件在检查后恢复，Vue 构建和浏览器验收继续通过。此证据覆盖同输入再生成及人工文件保护，不覆盖 schema 变更后的有意升级、应用 Worker 或容量实测，F02 仍未关闭。

应用自有业务代码再生成保护增量：独立生成应用完成 Catalog 模块首次接入后，在未受管的 `Product.manual.cs` 增加可编译业务策略，再执行 `apply-module-integration`。SQL Server/MySQL 本地真实栈 2/2 通过；两库的 `application-crud-module/manual-repeat.json` 均报告六个受管模块产物 `Unchanged`，`manual-build.json` 的 Release 构建退出码为 0，人工业务文件及宿主文件字节保持。聚焦测试 17/17 通过，其中故意改写人工文件的注入执行器会被验收拒绝。此证据覆盖同一 Schema 的模块重复接入，不覆盖 Schema 有意升级后的业务迁移或自动合并人工修改；F02 仍未关闭。

Schema 有意变更的源码升级增量：独立打包应用在既有 Product Schema 增加可空 `Description` 后，重跑应用自带 CLI，14 个受管源产物中 10 个 `Update`、4 个 `Unchanged`，SQL Server/MySQL 建表草案、后端契约与 OpenAPI 均含新字段；再次执行全部 `Unchanged`。模块接入更新 4 个产物，重复执行六个全部 `Unchanged`；人工业务文件、模块入口、授权贡献者、Composition 与 Vue 路由字节保持，API Release 构建通过。验收报告位于 `.tmp/template-real-stack/application-crud-schema-source-upgrade/`，明确 `databaseMigrationApplied=false`。这项单独验收只证明源码可受控升级；既有表的实际迁移及 HTTP 结果见下段。

既有业务表升级增量：独立生成应用在旧版 API 下先持久化一条商品，停机后升级受管源码，应用再显式采纳成对的 `002_AddProductDescription.sql`；已部署的 `001_CreateProduct.sql` 与人工业务文件保持原字节。SQL Server 2022 CU14 与 MySQL 8.0 本地真实栈各曾通过 1/1：`002` 首次执行 1 项、正常复跑 0 项；测试库精确撤销其 DbUp journal 行后，保留新增列重跑执行 1 项，再次复跑 0 项。升级验收还要求 SQL Server 模拟“列已添加、元数据注释未添加”的部分完成状态，并在重跑后检查注释恢复；这一新增断言的通过状态须按当前源码的新鲜运行结果判定。重启 API 并重新取得同一租户会话后，旧商品读回 `description=null`，可更新描述；携带描述的新建请求和省略描述的旧请求均返回 201。两库报告位于 `.tmp/template-real-stack/<provider>/application-crud-live-upgrade/`，`result.json` 标记 `databaseMigrationApplied=true`。生成器仍只产出建表草案，不会自动改写已采用的 `001` 或自动发布增量迁移；本次 `002` 是该样例经评审显式采纳的脚本，不代表任意 Schema 差异都能自动迁移。F02 仍需按其余未覆盖能力继续验收。

应用 Worker 运行增量：独立应用真实栈用例现于迁移后构建并启动应用自有 Worker，先执行 Tenancy 事件版本的空 Outbox 扫描，要求返回 `outbox.version_retirement.safe` 且待处理、死信数量均为零；常驻进程须通过 `/health/live` 和 `/health/ready`，持续运行到 API、客户端和浏览器验收结束，并检查关键后台故障日志。本地 SQL Server 2022 CU14 与 MySQL 8.0 均完成该流程；由于本机 Docker Hub 拉取令牌失败，本地 Redis 使用缓存的 8.6 镜像，CI 仍固定使用 MySQL 8.4 与 Redis 7.4。此证据覆盖双库启动、数据库查询及后台进程稳定性，不覆盖非空 Outbox 事件交付、应用自有 Native AOT 或容量实测，F02 仍未关闭。

应用 Worker 非空 Outbox 增量：双库真实栈在 Worker 启动前写入一条合法 `fullnet.tenancy.tenant.changed` MemoryPack 消息，并按消息 ID 确认初始 `Attempts=0`、未处理、非死信；应用自有 Worker 启动后，同一消息首次领取即写入已处理、非死信终态，租约及下次重试字段清空。测试探针只存在于验收工作区，使用生成应用分发的框架程序集生成载荷；SQL Server 与 MySQL 本地用例均通过。此证据只覆盖 Minimal 预设的该条合法事件路由及成功终态，不外推到其他 Handler、失败重试、Kafka、应用自有 Native AOT 或容量实测，F02 仍未关闭。

应用业务 Outbox 到投影增量：`66bcb227` 的[独立生成应用双库 CI](https://github.com/yan041108/Full.NET/actions/runs/37150305798)成功，模板测试 437/437、零失败/跳过。SQL Server/MySQL 各用真实租户令牌通过 Organization API 创建单位，核对业务事务写出的 `fullnet.organization.unit.changed` MemoryPack 消息，并在应用自有 Worker 运行后按消息 ID 确认首次处理、非死信、租约与重试字段清空；Identity 的同租户同单位投影名称、版本和启用状态与 API 创建结果一致。两库的 `worker-business-outbox.json` 均记录这一终态，不包含令牌。本地 Docker Engine 恢复后，使用 SQL Server 2022 CU14、MySQL 8.0 与缓存的 Redis 8.6 镜像重跑，双库真实栈 2/2 通过；此前一次数据库超时不能计为通过，但复跑未重现。不据此成功路径外推失败重试、其他事件路由、应用自有 Native AOT 或容量实测，F02 仍未关闭。
