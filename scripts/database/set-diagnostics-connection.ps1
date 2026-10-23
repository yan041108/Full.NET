param([Parameter(Mandatory=$true)][ValidateSet('SqlServer','MySql')][string]$Provider)
$ErrorActionPreference = 'Stop'
# 连接串通过隐藏输入接收，不进入命令参数或历史；User Secrets 仅供本机开发，文件不加密。
$secure = Read-Host '输入诊断账户连接串（隐藏输入）' -AsSecureString
$pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $connection = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    if ([string]::IsNullOrWhiteSpace($connection)) { throw '连接串不能为空。' }
    $base = if ($env:OS -eq 'Windows_NT') { Join-Path $env:APPDATA 'Microsoft/UserSecrets' } else { Join-Path $env:HOME '.microsoft/usersecrets' }
    $directory = Join-Path $base 'fullnet-database-diagnostics'
    $file = Join-Path $directory 'secrets.json'
    $values = @{}
    if (Test-Path -LiteralPath $file) {
        $previous = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        foreach ($property in $previous.PSObject.Properties) { $values[$property.Name] = $property.Value }
    }
    $values["ConnectionStrings:$Provider"] = $connection
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText($file, ($values | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    if ($env:OS -ne 'Windows_NT') { & chmod 700 $directory; & chmod 600 $file }
    Write-Output "已保存 $Provider 本机诊断配置（未输出凭据）。"
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    $connection = $null
    $secure.Dispose()
}
