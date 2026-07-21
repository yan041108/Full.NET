# Full.NET SQL 静态门禁：命名 + 破坏性变更安全。
# 命名规则不在此脚本重复实现；破坏性规则见 scripts/sql/validate-sql-safety.mjs。
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'
Set-Location $RepositoryRoot

Write-Host '== SQL naming governance =='
pnpm test:naming
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host '== SQL safety (destructive DDL / bare writes) =='
pnpm test:sql-safety
exit $LASTEXITCODE
