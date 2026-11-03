using Full.NET.Abstractions.Time;
using Full.NET.Data.Abstractions;
using Full.NET.Modules.CodeGeneration.Configuration;
using Full.NET.Modules.CodeGeneration.Features.ManageHostRuns;
using Full.NET.Modules.CodeGeneration.Persistence;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Full.NET.UnitTests.CodeGeneration;

/// <summary>元数据用替身隔离，包字节、锁、大小和完整性仍经真实归档实现。</summary>
internal sealed class CodeGenerationArtifactTestStore : IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("fullnet-codegen-archives-").FullName;
    public Dictionary<Guid, CodeGenerationArchiveRecord> Records { get; } = [];
    public CodeGenerationArtifactOptions Options { get; }
    public CodeGenerationArtifactStore Store { get; }

    public CodeGenerationArtifactTestStore(IClock clock)
    {
        Options = new() { RootDirectory = Root };
        var queries = Substitute.For<IQueryExecutor>();
        var commands = Substitute.For<ICommandExecutor>();
        queries.QuerySingleOrDefaultAsync<CodeGenerationArchiveRecord>(CodeGenerationArchiveSql.Find, Arg.Any<object?>(), Arg.Any<CancellationToken>())
            .Returns(call => Records.GetValueOrDefault(ReadSqlParameter<Guid>(call.ArgAt<object>(1), "Id")));
        commands.ExecuteAsync(CodeGenerationArchiveSql.Insert, Arg.Any<object?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var p = call.ArgAt<object>(1);
            var record = new CodeGenerationArchiveRecord { Id = ReadSqlParameter<Guid>(p, "Id"), SchemaJson = ReadSqlParameter<string>(p, "SchemaJson"),
                SchemaSha256 = ReadSqlParameter<string>(p, "SchemaSha256"), GeneratorVersion = ReadSqlParameter<string>(p, "GeneratorVersion"),
                ManifestSha256 = ReadSqlParameter<string>(p, "ManifestSha256"), ZipSha256 = ReadSqlParameter<string>(p, "ZipSha256"),
                FileName = ReadSqlParameter<string>(p, "FileName"), ByteLength = ReadSqlParameter<long>(p, "ByteLength"), ExpiresAtUtc = ReadSqlParameter<DateTimeOffset>(p, "ExpiresAtUtc") };
            Records.Add(record.Id, record);
            return 1;
        });
        Store = new(queries, commands, Microsoft.Extensions.Options.Options.Create(Options), clock);
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
