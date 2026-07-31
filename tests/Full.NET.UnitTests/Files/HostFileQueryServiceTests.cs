using Full.NET.Data.Abstractions;
using Full.NET.Modules.Files.Features.ManageHostFiles;
using Microsoft.Extensions.Options;

namespace Full.NET.UnitTests.Files;

[TestClass]
public sealed class HostFileQueryServiceTests
{
    [TestMethod]
    public async Task List_keeps_extreme_page_offset_outside_int_overflow()
    {
        var queryExecutor = new RecordingQueryExecutor();
        var service = new HostFileQueryService(
            queryExecutor,
            Options.Create(new DatabaseOptions
            {
                Provider = DatabaseProvider.SqlServer,
            }));

        _ = await service.ListAsync(int.MaxValue, 100);

        var offset = queryExecutor.Parameters!
            .GetType()
            .GetProperty("Offset")!
            .GetValue(queryExecutor.Parameters);
        Assert.IsInstanceOfType<long>(offset);
        Assert.AreEqual(((long)int.MaxValue - 1) * 100, (long)offset);
    }

    private sealed class RecordingQueryExecutor : IQueryExecutor
    {
        public object? Parameters { get; private set; }

        public Task<T?> QuerySingleOrDefaultAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<T?>(typeof(T) == typeof(long)
                ? (T)(object)0L
                : default);

        public Task<IReadOnlyList<T>> QueryAsync<T>(
            SqlStatement statement,
            object? parameters = null,
            CancellationToken cancellationToken = default)
        {
            Parameters = parameters;
            return Task.FromResult<IReadOnlyList<T>>(Array.Empty<T>());
        }
    }
}
