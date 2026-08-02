extern alias codegencli;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Full.NET.Data.CodeGeneration.Generation;
using Full.NET.Data.CodeGeneration.Integration;
using CodeGenerationCli =
    codegencli::Full.NET.CodeGeneration.Cli.CodeGenerationCli;
using CrudSchemaDocument =
    codegencli::Full.NET.CodeGeneration.Cli.CrudSchemaDocument;

namespace Full.NET.IntegrationTests.CodeGeneration;

[TestClass]
[DoNotParallelize]
public sealed class ModuleIntegrationCompilationTests
{
    [TestMethod]
    public async Task Existing_settings_module_compiles_generated_backend_without_repository_writes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testRoot = CreateTestRoot();
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            schemaPath,
            CreateSchemaJson(
                "settings",
                "compile_probe",
                "Full.NET.Modules.Settings",
                "GeneratedProbe",
                "generated-probes",
                "generated_probes"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Settings",
                "src/Modules/Full.NET.Modules.Settings/Full.NET.Modules.Settings.csproj",
                "src/Modules/Full.NET.Modules.Settings/SettingsModule.cs"),
            new UTF8Encoding(false, true));
        var before = CaptureTargetFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                ValidationArguments(
                    schemaPath,
                    repositoryRoot,
                    targetPath),
                output,
                error);

            Assert.AreEqual(0, exitCode, error.ToString());
            StringAssert.Contains(
                output.ToString(),
                "Validated ModuleCompilation "
                + "src/Modules/Full.NET.Modules.Settings/"
                + "Full.NET.Modules.Settings.csproj");
            Assert.AreEqual(string.Empty, error.ToString());
            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureTargetFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Explicit_soft_delete_backend_compiles_without_client_controlled_audit_fields()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testRoot = CreateTestRoot();
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            schemaPath,
            CreateExplicitLifecycleSchemaJson(),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Settings",
                "src/Modules/Full.NET.Modules.Settings/Full.NET.Modules.Settings.csproj",
                "src/Modules/Full.NET.Modules.Settings/SettingsModule.cs"),
            new UTF8Encoding(false, true));
        await AssertGeneratedClientSyntaxAsync(schemaPath, testRoot);
        var before = CaptureTargetFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                ValidationArguments(
                    schemaPath,
                    repositoryRoot,
                    targetPath),
                output,
                error);

            Assert.AreEqual(0, exitCode, error.ToString());
            StringAssert.Contains(
                output.ToString(),
                "Validated ModuleCompilation "
                + "src/Modules/Full.NET.Modules.Settings/"
                + "Full.NET.Modules.Settings.csproj");
            Assert.AreEqual(string.Empty, error.ToString());
            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureTargetFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Missing_module_dependencies_return_sanitized_diagnostics_and_cleanup()
    {
        var testRoot = CreateTestRoot();
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var moduleDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "Modules",
            "Acme.Modules.Catalog");
        Directory.CreateDirectory(moduleDirectory);
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            Path.Combine(
                moduleDirectory,
                "Acme.Modules.Catalog.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <FrameworkReference Include="Microsoft.AspNetCore.App" />
              </ItemGroup>
            </Project>
            """,
            new UTF8Encoding(false, true));
        File.WriteAllText(
            schemaPath,
            CreateSchemaJson(
                "catalog",
                "product",
                "Acme.Modules.Catalog",
                "Product",
                "products",
                "products"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Catalog",
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs"),
            new UTF8Encoding(false, true));
        var before = CaptureAllFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                ValidationArguments(
                    schemaPath,
                    repositoryRoot,
                    targetPath),
                output,
                error);

            Assert.AreEqual(2, exitCode);
            StringAssert.Contains(error.ToString(), "error CS");
            Assert.IsFalse(error.ToString().Contains(
                repositoryRoot,
                StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(error.ToString().Contains(
                testRoot,
                StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(string.Empty, output.ToString());
            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureAllFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Cancellation_stops_build_and_removes_temporary_projection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var testRoot = CreateTestRoot();
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            schemaPath,
            CreateSchemaJson(
                "settings",
                "cancel_probe",
                "Full.NET.Modules.Settings",
                "CancellationProbe",
                "cancellation-probes",
                "cancellation_probes"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Settings",
                "src/Modules/Full.NET.Modules.Settings/Full.NET.Modules.Settings.csproj",
                "src/Modules/Full.NET.Modules.Settings/SettingsModule.cs"),
            new UTF8Encoding(false, true));
        var before = CaptureTargetFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(100));
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                CodeGenerationCli.RunAsync(
                    ValidationArguments(
                        schemaPath,
                        repositoryRoot,
                        targetPath),
                    output,
                    error,
                    cancellation.Token));

            Assert.AreEqual(string.Empty, output.ToString());
            Assert.AreEqual(string.Empty, error.ToString());
            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureTargetFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Organization_owned_explicit_backend_compiles_with_organization_references()
    {
        var sourceRepositoryRoot = FindRepositoryRoot();
        var testRoot = CreateTestRoot();
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var moduleDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "Modules",
            "Acme.Modules.Catalog");
        Directory.CreateDirectory(moduleDirectory);
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            schemaPath,
            CreateOrganizationOwnedExplicitSchemaJson(),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Catalog",
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            Path.Combine(
                moduleDirectory,
                "Acme.Modules.Catalog.csproj"),
            CreateCompilableModuleProject(
                sourceRepositoryRoot,
                includeOrganizationContracts: true),
            new UTF8Encoding(false, true));
        var before = CaptureAllFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();

            var exitCode = await CodeGenerationCli.RunAsync(
                ValidationArguments(
                    schemaPath,
                    repositoryRoot,
                    targetPath),
                output,
                error);

            Assert.AreEqual(0, exitCode, error.ToString());
            StringAssert.Contains(
                output.ToString(),
                "Validated ModuleCompilation "
                + "src/Modules/Acme.Modules.Catalog/"
                + "Acme.Modules.Catalog.csproj");
            Assert.AreEqual(string.Empty, error.ToString());
            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureAllFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task Existing_generated_entity_sources_are_replaced_during_validation()
    {
        var sourceRepositoryRoot = FindRepositoryRoot();
        var testRoot = CreateTestRoot();
        var repositoryRoot = Path.Combine(testRoot, "repository");
        var moduleDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "Modules",
            "Acme.Modules.Catalog");
        Directory.CreateDirectory(moduleDirectory);
        var schemaPath = Path.Combine(testRoot, "schema.json");
        var targetPath = Path.Combine(testRoot, "target.json");
        File.WriteAllText(
            schemaPath,
            CreateSchemaJson(
                "catalog",
                "product",
                "Acme.Modules.Catalog",
                "Product",
                "products",
                "products"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            targetPath,
            CreateTargetJson(
                "Catalog",
                "src/Modules/Acme.Modules.Catalog/Acme.Modules.Catalog.csproj",
                "src/Modules/Acme.Modules.Catalog/CatalogModule.cs"),
            new UTF8Encoding(false, true));
        File.WriteAllText(
            Path.Combine(
                moduleDirectory,
                "Acme.Modules.Catalog.csproj"),
            CreateCompilableModuleProject(sourceRepositoryRoot),
            new UTF8Encoding(false, true));
        var schema = await CrudSchemaDocument.LoadAsync(
            schemaPath,
            CancellationToken.None);
        foreach (var artifact in
                 ModuleIntegrationBackendWorkspace.CreateArtifacts(schema))
        {
            var path = Path.Combine(
                moduleDirectory,
                artifact.RelativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                artifact.Content,
                new UTF8Encoding(false, true));
        }

        var before = CaptureAllFiles(repositoryRoot);
        var temporaryBuilds = CaptureTemporaryBuildDirectories();
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var output = new StringWriter();
                using var error = new StringWriter();

                var exitCode = await CodeGenerationCli.RunAsync(
                    ValidationArguments(
                        schemaPath,
                        repositoryRoot,
                        targetPath),
                    output,
                    error);

                Assert.AreEqual(0, exitCode, error.ToString());
                StringAssert.Contains(
                    output.ToString(),
                    "Validated ModuleCompilation");
                Assert.AreEqual(string.Empty, error.ToString());
            }

            CollectionAssert.AreEquivalent(
                before.ToArray(),
                CaptureAllFiles(repositoryRoot).ToArray());
            CollectionAssert.AreEquivalent(
                temporaryBuilds,
                CaptureTemporaryBuildDirectories());
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static string[] ValidationArguments(
        string schemaPath,
        string repositoryRoot,
        string targetPath) =>
        [
            "validate-module-integration",
            "--schema",
            schemaPath,
            "--repository",
            repositoryRoot,
            "--target",
            targetPath,
        ];

    private static string CreateSchemaJson(
        string moduleKey,
        string entityKey,
        string rootNamespace,
        string clrTypeName,
        string apiResourceName,
        string permissionResourceName) =>
        $$"""
        {
          "ownerKey": "acme",
          "moduleKey": "{{moduleKey}}",
          "entityKey": "{{entityKey}}",
          "databaseTableName": "acme_{{moduleKey}}_{{entityKey}}",
          "rootNamespace": "{{rootNamespace}}",
          "clrTypeName": "{{clrTypeName}}",
          "apiResourceName": "{{apiResourceName}}",
          "permissionResourceName": "{{permissionResourceName}}",
          "dataScope": "TenantRequired",
          "hasVersion": true,
          "columns": [
            {
              "databaseName": "Id",
              "clrPropertyName": "Id",
              "jsonPropertyName": "id",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "TenantId",
              "clrPropertyName": "TenantId",
              "jsonPropertyName": "tenantId",
              "scalarType": "Uuid"
            },
            {
              "databaseName": "Name",
              "clrPropertyName": "Name",
              "jsonPropertyName": "name",
              "scalarType": "String",
              "maxLength": 200
            },
            {
              "databaseName": "IsActive",
              "clrPropertyName": "IsActive",
              "jsonPropertyName": "isActive",
              "scalarType": "Boolean"
            },
            {
              "databaseName": "Version",
              "clrPropertyName": "Version",
              "jsonPropertyName": "version",
              "scalarType": "Int64"
            },
            {
              "databaseName": "CreatedAtUtc",
              "clrPropertyName": "CreatedAtUtc",
              "jsonPropertyName": "createdAtUtc",
              "scalarType": "DateTimeUtc"
            }
          ]
        }
        """;

    private static string CreateTargetJson(
        string moduleName,
        string moduleProjectPath,
        string moduleEntryPointPath) =>
        $$"""
        {
          "moduleName": "{{moduleName}}",
          "moduleProjectPath": "{{moduleProjectPath}}",
          "moduleEntryPointPath": "{{moduleEntryPointPath}}",
          "compositionProjectPath": "src/Composition/Full.NET.Composition/Full.NET.Composition.csproj",
          "compositionCatalogPath": "src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs",
          "vueRouterPath": "ui/admin/src/router/index.ts",
          "layuiRouterPath": "ui/admin-layui/js/core/route-controllers.js"
        }
        """;

    private static string CreateExplicitLifecycleSchemaJson() =>
        """
        {
          "ownerKey": "acme",
          "moduleKey": "settings",
          "entityKey": "lifecycle_probe",
          "databaseTableName": "acme_settings_lifecycle_probe",
          "rootNamespace": "Full.NET.Modules.Settings",
          "clrTypeName": "LifecycleProbe",
          "apiResourceName": "lifecycle-probes",
          "permissionResourceName": "lifecycle_probes",
          "dataScope": "tenant.required",
          "entityCapabilities": {
            "deleteMode": "soft.delete",
            "hasCreatedAudit": true,
            "hasUpdatedAudit": true,
            "hasDeletedAudit": true,
            "hasVersion": true,
            "ownershipMode": "none"
          },
          "columns": [
            {
              "databaseName": "Id",
              "clrPropertyName": "Id",
              "jsonPropertyName": "id",
              "scalarType": "uuid"
            },
            {
              "databaseName": "TenantId",
              "clrPropertyName": "TenantId",
              "jsonPropertyName": "tenantId",
              "scalarType": "uuid"
            },
            {
              "databaseName": "Name",
              "clrPropertyName": "Name",
              "jsonPropertyName": "name",
              "scalarType": "string",
              "maxLength": 200
            },
            {
              "databaseName": "Version",
              "clrPropertyName": "Version",
              "jsonPropertyName": "version",
              "scalarType": "int64"
            },
            {
              "databaseName": "CreatedAtUtc",
              "clrPropertyName": "CreatedAtUtc",
              "jsonPropertyName": "createdAtUtc",
              "scalarType": "date.time.utc"
            },
            {
              "databaseName": "CreatedById",
              "clrPropertyName": "CreatedById",
              "jsonPropertyName": "createdById",
              "scalarType": "uuid"
            },
            {
              "databaseName": "UpdatedAtUtc",
              "clrPropertyName": "UpdatedAtUtc",
              "jsonPropertyName": "updatedAtUtc",
              "scalarType": "date.time.utc",
              "isNullable": true
            },
            {
              "databaseName": "UpdatedById",
              "clrPropertyName": "UpdatedById",
              "jsonPropertyName": "updatedById",
              "scalarType": "uuid",
              "isNullable": true
            },
            {
              "databaseName": "IsDeleted",
              "clrPropertyName": "IsDeleted",
              "jsonPropertyName": "isDeleted",
              "scalarType": "boolean"
            },
            {
              "databaseName": "DeletedAtUtc",
              "clrPropertyName": "DeletedAtUtc",
              "jsonPropertyName": "deletedAtUtc",
              "scalarType": "date.time.utc",
              "isNullable": true
            },
            {
              "databaseName": "DeletedById",
              "clrPropertyName": "DeletedById",
              "jsonPropertyName": "deletedById",
              "scalarType": "uuid",
              "isNullable": true
            }
          ]
        }
        """;

    private static string CreateOrganizationOwnedExplicitSchemaJson() =>
        """
        {
          "ownerKey": "acme",
          "moduleKey": "catalog",
          "entityKey": "org_probe",
          "databaseTableName": "acme_catalog_org_probe",
          "rootNamespace": "Acme.Modules.Catalog",
          "clrTypeName": "OrgProbe",
          "apiResourceName": "org-probes",
          "permissionResourceName": "org_probes",
          "dataScope": "tenant.required",
          "entityCapabilities": {
            "deleteMode": "soft.delete",
            "hasCreatedAudit": true,
            "hasUpdatedAudit": true,
            "hasDeletedAudit": true,
            "hasVersion": true,
            "ownershipMode": "organization.unit"
          },
          "columns": [
            {
              "databaseName": "Id",
              "clrPropertyName": "Id",
              "jsonPropertyName": "id",
              "scalarType": "uuid"
            },
            {
              "databaseName": "TenantId",
              "clrPropertyName": "TenantId",
              "jsonPropertyName": "tenantId",
              "scalarType": "uuid"
            },
            {
              "databaseName": "OrganizationUnitId",
              "clrPropertyName": "OrganizationUnitId",
              "jsonPropertyName": "organizationUnitId",
              "scalarType": "uuid"
            },
            {
              "databaseName": "Name",
              "clrPropertyName": "Name",
              "jsonPropertyName": "name",
              "scalarType": "string",
              "maxLength": 200
            },
            {
              "databaseName": "Version",
              "clrPropertyName": "Version",
              "jsonPropertyName": "version",
              "scalarType": "int64"
            },
            {
              "databaseName": "CreatedAtUtc",
              "clrPropertyName": "CreatedAtUtc",
              "jsonPropertyName": "createdAtUtc",
              "scalarType": "date.time.utc"
            },
            {
              "databaseName": "CreatedById",
              "clrPropertyName": "CreatedById",
              "jsonPropertyName": "createdById",
              "scalarType": "uuid"
            },
            {
              "databaseName": "UpdatedAtUtc",
              "clrPropertyName": "UpdatedAtUtc",
              "jsonPropertyName": "updatedAtUtc",
              "scalarType": "date.time.utc",
              "isNullable": true
            },
            {
              "databaseName": "UpdatedById",
              "clrPropertyName": "UpdatedById",
              "jsonPropertyName": "updatedById",
              "scalarType": "uuid",
              "isNullable": true
            },
            {
              "databaseName": "IsDeleted",
              "clrPropertyName": "IsDeleted",
              "jsonPropertyName": "isDeleted",
              "scalarType": "boolean"
            },
            {
              "databaseName": "DeletedAtUtc",
              "clrPropertyName": "DeletedAtUtc",
              "jsonPropertyName": "deletedAtUtc",
              "scalarType": "date.time.utc",
              "isNullable": true
            },
            {
              "databaseName": "DeletedById",
              "clrPropertyName": "DeletedById",
              "jsonPropertyName": "deletedById",
              "scalarType": "uuid",
              "isNullable": true
            }
          ]
        }
        """;

    private static async Task AssertGeneratedClientSyntaxAsync(
        string schemaPath,
        string testRoot)
    {
        var schema = await CrudSchemaDocument.LoadAsync(
            schemaPath,
            CancellationToken.None);
        foreach (var artifact in CrudArtifactGenerator.Generate(schema)
                     .Where(item =>
                         item.RelativePath.EndsWith(
                             ".ts",
                             StringComparison.Ordinal)
                         || item.RelativePath.EndsWith(
                             ".js",
                             StringComparison.Ordinal)))
        {
            var extension = Path.GetExtension(artifact.RelativePath);
            var syntaxExtension = extension == ".js" ? ".mjs" : extension;
            var syntaxPath = Path.Combine(
                testRoot,
                Path.GetFileNameWithoutExtension(artifact.RelativePath)
                + syntaxExtension);
            File.WriteAllText(
                syntaxPath,
                artifact.Content,
                new UTF8Encoding(false, true));
            var startInfo = new ProcessStartInfo("node")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (extension == ".ts")
            {
                startInfo.ArgumentList.Add("--experimental-strip-types");
            }

            startInfo.ArgumentList.Add("--check");
            startInfo.ArgumentList.Add(syntaxPath);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(
                    "无法启动 Node.js 客户端语法校验。");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.AreEqual(
                0,
                process.ExitCode,
                $"{artifact.RelativePath}{Environment.NewLine}"
                + standardOutput
                + standardError);
        }
    }

    private static string CreateCompilableModuleProject(
        string repositoryRoot,
        bool includeOrganizationContracts = false)
    {
        string Project(string relativePath) =>
            Path.Combine(
                repositoryRoot,
                relativePath.Replace(
                    '/',
                    Path.DirectorySeparatorChar));

        var organizationReference = includeOrganizationContracts
            ? $$"""

                <ProjectReference Include="{{Project("src/Modules/Full.NET.Modules.Organization.Contracts/Full.NET.Modules.Organization.Contracts.csproj")}}" />
            """
            : string.Empty;

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <FrameworkReference Include="Microsoft.AspNetCore.App" />
            <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Abstractions/Full.NET.Abstractions.csproj")}}" />
            <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Data.Abstractions/Full.NET.Data.Abstractions.csproj")}}" />
            <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Hosting/Full.NET.Hosting.csproj")}}" />
            <ProjectReference Include="{{Project("src/BuildingBlocks/Full.NET.Modularity/Full.NET.Modularity.csproj")}}" />
            <ProjectReference Include="{{Project("src/Modules/Full.NET.Modules.Identity.Contracts/Full.NET.Modules.Identity.Contracts.csproj")}}" />{{organizationReference}}
          </ItemGroup>
        </Project>
        """;
    }

    private static Dictionary<string, string> CaptureTargetFiles(
        string repositoryRoot)
    {
        var moduleRoot = Path.Combine(
            repositoryRoot,
            "src",
            "Modules",
            "Full.NET.Modules.Settings");
        var files = Directory
            .GetFiles(
                moduleRoot,
                "*",
                SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(repositoryRoot, path),
                HashFile,
                StringComparer.Ordinal);
        foreach (var relativePath in new[]
        {
            "src/Composition/Full.NET.Composition/Full.NET.Composition.csproj",
            "src/Composition/Full.NET.Composition/FullNetModuleCatalog.cs",
            "ui/admin/src/router/index.ts",
            "ui/admin-layui/js/core/route-controllers.js",
        })
        {
            files.Add(
                relativePath,
                HashFile(Path.Combine(
                    repositoryRoot,
                    relativePath.Replace(
                        '/',
                        Path.DirectorySeparatorChar))));
        }

        return files;
    }

    private static Dictionary<string, string> CaptureAllFiles(
        string repositoryRoot) =>
        Directory
            .GetFiles(
                repositoryRoot,
                "*",
                SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetRelativePath(repositoryRoot, path),
                HashFile,
                StringComparer.Ordinal);

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string[] CaptureTemporaryBuildDirectories() =>
        Directory
            .GetDirectories(
                Path.GetTempPath(),
                "fullnet-codegen-module-build-*",
                SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"fullnet-codegen-module-compile-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Full.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位仓库根目录。");
    }
}
