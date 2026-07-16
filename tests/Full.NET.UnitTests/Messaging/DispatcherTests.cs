using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modularity.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Messaging;

[TestClass]
public sealed class DispatcherTests
{
    [TestMethod]
    public async Task Transactional_command_executes_inside_transaction()
    {
        var transaction = new RecordingTransaction();
        await using var provider = new ServiceCollection()
            .AddSingleton<ICommandHandler<EchoCommand, string>, EchoHandler>()
            .AddSingleton<ICommandTransaction>(transaction)
            .AddScoped<ICommandDispatcher, CommandDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<EchoCommand, string>(new EchoCommand("value"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("value", result.Value);
        Assert.IsTrue(transaction.Executed);
    }

    [TestMethod]
    public async Task Non_transactional_command_bypasses_transaction()
    {
        var transaction = new RecordingTransaction();
        await using var provider = new ServiceCollection()
            .AddSingleton<ICommandHandler<PlainEchoCommand, string>, PlainEchoHandler>()
            .AddSingleton<ICommandTransaction>(transaction)
            .AddScoped<ICommandDispatcher, CommandDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<PlainEchoCommand, string>(new PlainEchoCommand("plain"));

        Assert.AreEqual("plain", result.Value);
        Assert.IsFalse(transaction.Executed);
    }

    [TestMethod]
    public async Task Query_dispatches_to_registered_handler()
    {
        await using var provider = new ServiceCollection()
            .AddSingleton<IQueryHandler<EchoQuery, string>, EchoQueryHandler>()
            .AddScoped<IQueryDispatcher, QueryDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<IQueryDispatcher>()
            .SendAsync<EchoQuery, string>(new EchoQuery("query"));

        Assert.AreEqual("query", result.Value);
    }

    [TestMethod]
    public void AddFullNetModularity_registers_scoped_dispatchers()
    {
        var services = new ServiceCollection().AddFullNetModularity();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();
        var otherScope = secondScope.ServiceProvider.GetRequiredService<ICommandDispatcher>();

        Assert.AreSame(first, sameScope);
        Assert.AreNotSame(first, otherScope);
        Assert.IsInstanceOfType<QueryDispatcher>(
            firstScope.ServiceProvider.GetRequiredService<IQueryDispatcher>());
    }

    private sealed record EchoCommand(string Value) : ITransactionalCommand<string>;

    private sealed class EchoHandler : ICommandHandler<EchoCommand, string>
    {
        public Task<Result<string>> HandleAsync(
            EchoCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<string>.Success(command.Value));
    }

    private sealed record PlainEchoCommand(string Value) : ICommand<string>;

    private sealed class PlainEchoHandler : ICommandHandler<PlainEchoCommand, string>
    {
        public Task<Result<string>> HandleAsync(
            PlainEchoCommand command,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<string>.Success(command.Value));
    }

    private sealed record EchoQuery(string Value) : IQuery<string>;

    private sealed class EchoQueryHandler : IQueryHandler<EchoQuery, string>
    {
        public Task<Result<string>> HandleAsync(
            EchoQuery query,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<string>.Success(query.Value));
    }

    private sealed class RecordingTransaction : ICommandTransaction
    {
        public bool Executed { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return await action(cancellationToken);
        }
    }
}
