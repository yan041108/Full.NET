param(
    [Parameter(Mandatory = $true)]
    [int]$Through
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $repoRoot

dotnet build tests/Full.NET.IntegrationTests/Full.NET.IntegrationTests.csproj -c Release -v q | Out-Null

$code = @"
using DbUp;
using Full.NET.Data.Abstractions;
using Full.NET.Data.MySql;
using Full.NET.Migrations.DbUp;
using System.Text.RegularExpressions;

var connectionString = await Full.NET.IntegrationTests.SharedDatabaseFixture.CreateMySqlDatabaseAsync();
var through = $Through;
var result = DeployChanges.To.MySqlDatabase(
        MySqlConnectionStringPolicy.Create(connectionString, MySqlGuidStorageMode.Binary16, allowUserVariables: true))
    .WithPreprocessor(new PublishedMySqlPreprocessor())
    .WithScriptsEmbeddedInAssembly(
        typeof(DbUpMigrationRunner).Assembly,
        name => name.Contains(".Migrations.MySql.", StringComparison.Ordinal)
            && Enumerable.Range(1, through).Any(number =>
                name.Contains($".{number:000}_", StringComparison.Ordinal)))
    .WithVariable("UuidContractMaintenanceMode", "1")
    .WithVariable("UuidContractBackupVerified", "1")
    .WithVariable("UuidContractLegacyWritersStopped", "1")
    .WithVariable("UuidContractDestructiveDdlApprovalId", "test-uuid-contract-009")
    .WithVariable("PreV1NamingContractMaintenanceMode", "1")
    .WithVariable("PreV1NamingContractBackupVerified", "1")
    .WithVariable("PreV1NamingContractLegacyWritersStopped", "1")
    .WithVariable("PreV1NamingContractLegacyOutboxDrained", "1")
    .WithVariable("PreV1NamingContractDestructiveDdlApprovalId", "test-naming-contract-011")
    .LogToConsole()
    .WithExecutionTimeout(TimeSpan.FromSeconds(300))
    .Build()
    .PerformUpgrade();

if (result.Successful)
{
    Console.WriteLine($"OK through {through}: {result.Scripts.Count()} scripts");
}
else
{
    Console.Error.WriteLine($"FAIL through {through}: {result.Error}");
    Environment.Exit(1);
}

sealed class PublishedMySqlPreprocessor : DbUp.Engine.IScriptPreprocessor
{
    private static readonly HashSet<string> Digests = new(StringComparer.Ordinal)
    {
        "E1336EB2A3BC7E73949B43061F6F5BDFEC138DFCCAB8C013C880806C1CFBE1E1",
        "98F710F450610F1661F61A9A534D2539D5577DFEE3183BFBC0B64D43E7F5C3A5",
        "CD35465D5D07657B85A0B2F93C10552FEDE2905ED64FA5B82938D93B0D1FA7B5",
        "C913081680634BCCC8AF81228F159868BCA2AC663E17FAA6EC8B41D8316E5DD2",
        "55DB983CB230FB7C23CF783FC72B4873067BE27F00ECA4A4E2FCB03D9A7C7A2A",
        "288DB2350582D9101C2727D77BE386437E47B65CD4A395B1DE2F01D51FF49E93",
        "510B8892D9806F656E50CF722F3ABF78C51FB6A9219B88A89FD3466CAB93E6D7",
        "7ACFC9054E4FF2EB17E1312622480DFB2BE93F7FE1B45AC66AB896C4AC64C7DF",
        "41656877898E6F4B3F09FF96EA2E618A71EB2AF7D178ED90FB433636483EA115",
        "2CCF23981E326A768221F20DE3C5628AFC47C9270F9DFF77FB39F916DB9A26D4",
        "E4A53B26D2F05C5D8E72622C80FDF537FF467A62A0330E7594341DA6ACD1B606",
        "55560720FC03B2812A71132FFC92AE7B49313216CF8AED7D1788D8B0F9F30514",
        "09496E6184781BF50708FC39E7A70FC765A26A2A2C1BD2FFC79F75E7F2F51E3C",
        "AAEF2CDE67F0DF065558B0AADF42DC7CCC4F240CD2916E3A987ADE08A69E8414",
        "EF2BCBCBA4AE9B195206613411466631E31EAC2EDB4D5D035AF898D28917231B",
        "8B93ED88F9411B8C6BE48B5A7A4F14BEF1BFD3D348EC8340878CBF4F6DA1C461",
    };

    public string Process(string contents)
    {
        var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(contents.ReplaceLineEndings("\n"))));
        if (Digests.Contains(digest))
        {
            return Regex.Replace(contents, @"char\(36\)(?: CHARACTER SET ascii COLLATE ascii_general_ci| COLLATE utf8mb4_bin)?", "BINARY(16)", RegexOptions.IgnoreCase);
        }

        return Regex.Replace(
            contents,
            @"(?ms)^\s*ALTER\s+TABLE\s+fn_messaging_stream_ownership\s+ADD\s+CONSTRAINT\s+IF\s+NOT\s+EXISTS\s+CK_fn_messaging_stream_ownership_(?:SchemaVersion|CurrentOwner|PreviousOwner)\s+CHECK\s*\([^;]+;\s*",
            "-- 094 compatibility: constraints converge in migration 095.\n");
    }
}
"@

$tempDir = Join-Path $env:TEMP "fullnet-mysql-migrate-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tempDir | Out-Null
$program = Join-Path $tempDir 'Program.cs'
$proj = Join-Path $tempDir 'Diag.csproj'
Set-Content -Path $program -Value $code -Encoding UTF8
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$repoRoot\tests\Full.NET.IntegrationTests\Full.NET.IntegrationTests.csproj" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $proj -Encoding UTF8

dotnet run --project $proj -c Release
$exit = $LASTEXITCODE
Remove-Item -Recurse -Force $tempDir
Pop-Location
exit $exit
