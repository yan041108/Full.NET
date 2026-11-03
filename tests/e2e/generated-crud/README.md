# 实际生成 CRUD 页面验收

此夹具用当前 Release CLI 在系统临时目录生成 Schema 对应产物，使用同一 OpenAPI 入口生成客户端，然后运行真实 SFC。权限服务和 HTTP 使用受控夹具；结果不能作为真实 Host、身份服务或双库端到端证据。

先通过仓库资源入口编译 CLI，再执行以下 PowerShell 命令；目标须为空的系统临时子目录，或带有此夹具标记的既有目录。只链接当前 Vue 依赖，不复制仓库或依赖。

```powershell
node scripts/testing/run-build-with-resources.mjs --cpu 4 --memory-gib 6 -- dotnet build src/Tools/Full.NET.CodeGeneration.Cli/Full.NET.CodeGeneration.Cli.csproj --configuration Release -m:4
$fixtureRoot = Join-Path $env:TEMP 'fullnet-generated-workbench'
node tests/e2e/generated-crud/prepare-workbench.mjs $fixtureRoot
pnpm --dir ui/admin exec vue-tsc --project "$fixtureRoot/tsconfig.json" --noEmit
pnpm --dir tests/e2e/admin-parity exec playwright test --config "$fixtureRoot/playwright.config.mjs" --output "$fixtureRoot/browser-results"
```

执行后停止夹具 Vite/Edge 进程，核实绝对路径及标记后清理此临时目录；保留失败定位所需的最小原始记录。测试覆盖两种视口、精确大数、可空值、微秒日期原文保留与编辑后 UTC 转换、筛选/排序、前端校验、多语言和 axe，不关闭 axe 规则。

准备命令还支持 `--tree`、`--aggregate`、`--association`，分别验证层级/父节点、主从单次组合保存、关联候选及版本保存。各场景使用不同临时目录，依次运行：共用本地端口 15174。工作台可空布尔采用明确的三态单选，区分未设置、是、否。

可编译的后端夹具和旧版黄金产物由 `tests/Tools/Full.NET.CodeGeneration.Fixtures` 维护。通过资源入口运行 `dotnet run --project tests/Tools/Full.NET.CodeGeneration.Fixtures --configuration Release -- --check` 检查一致性，模板变化后用 `--write` 更新。生成器版本随模板行为显式演进；测试调用实际生成源码，禁止手工修补夹具掩盖生成错误。
