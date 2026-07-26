$logPath = 'G:\wwwroot\github_fork\Full.NET\.tmp\codex-verification-20260726\full-integration-final.log'
$exitPath = 'G:\wwwroot\github_fork\Full.NET\.tmp\codex-verification-20260726\full-integration-final.exit'

& dotnet `
    'G:\wwwroot\github_fork\Full.NET\tests\Full.NET.IntegrationTests\bin\Debug\net10.0\Full.NET.IntegrationTests.dll' `
    --no-ansi `
    --progress off `
    --minimum-expected-tests 172 `
    --timeout 60m *> $logPath

Set-Content -LiteralPath $exitPath -Value $LASTEXITCODE
