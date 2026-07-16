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

    [TestMethod]
    public async Task Command_behaviors_wrap_handler_in_registration_order()
    {
        var calls = new List<string>();
        await using var provider = new ServiceCollection()
            .AddSingleton<ICommandHandler<OrderedCommand, string>>(
                new OrderedCommandHandler(calls))
            .AddSingleton<IDispatchBehavior<OrderedCommand, string>>(
                new RecordingBehavior<OrderedCommand, string>("first", calls))
            .AddSingleton<IDispatchBehavior<OrderedCommand, string>>(
                new RecordingBehavior<OrderedCommand, string>("second", calls))
            .AddScoped<ICommandDispatcher, CommandDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<OrderedCommand, string>(new OrderedCommand());

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[]
            {
                "first:before",
                "second:before",
                "handler",
                "second:after",
                "first:after"
            },
            calls.ToArray());
    }

    [TestMethod]
    public async Task Query_behavior_wraps_handler()
    {
        var calls = new List<string>();
        await using var provider = new ServiceCollection()
            .AddSingleton<IQueryHandler<OrderedQuery, string>>(
                new OrderedQueryHandler(calls))
            .AddSingleton<IDispatchBehavior<OrderedQuery, string>>(
                new RecordingBehavior<OrderedQuery, string>("query", calls))
            .AddScoped<IQueryDispatcher, QueryDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<IQueryDispatcher>()
            .SendAsync<OrderedQuery, string>(new OrderedQuery());

        Assert.IsTrue(result.IsSuccess);
        CollectionAssert.AreEqual(
            new[] { "query:before", "query-handler", "query:after" },
            calls.ToArray());
    }

    [TestMethod]
    public async Task Rejecting_behavior_prevents_transaction_and_handler()
    {
        var transaction = new RecordingTransaction();
        var handler = new RejectableCommandHandler();
        await using var provider = new ServiceCollection()
            .AddSingleton<ICommandHandler<RejectableCommand, string>>(handler)
            .AddSingleton<ICommandTransaction>(transaction)
            .AddSingleton<IDispatchBehavior<RejectableCommand, string>>(
                new RejectingBehavior<RejectableCommand, string>())
            .AddScoped<ICommandDispatcher, CommandDispatcher>()
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<RejectableCommand, string>(new RejectableCommand());

        Assert.IsFalse(result.IsSuccess);
        var error = result.Error;
        Assert.IsNotNull(error);
        Assert.AreEqual("rejected", error.Code);
        Assert.IsFalse(transaction.Executed);
        Assert.IsFalse(handler.Executed);
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

    private sealed record OrderedCommand : ICommand<string>;

    private sealed class OrderedCommandHandler(IList<string> calls)
        : ICommandHandler<OrderedCommand, string>
    {
        public Task<Result<string>> HandleAsync(
            OrderedCommand command,
            CancellationToken cancellationToken)
        {
            calls.Add("handler");
            return Task.FromResult(Result<string>.Success("ordered"));
        }
    }

    private sealed record OrderedQuery : IQuery<string>;

    private sealed class OrderedQueryHandler(IList<string> calls)
        : IQueryHandler<OrderedQuery, string>
    {
        public Task<Result<string>> HandleAsync(
            OrderedQuery query,
            CancellationToken cancellationToken)
        {
            calls.Add("query-handler");
            return Task.FromResult(Result<string>.Success("ordered"));
        }
    }

    private sealed record RejectableCommand : ITransactionalCommand<string>;

    private sealed class RejectableCommandHandler
        : ICommandHandler<RejectableCommand, string>
    {
        public bool Executed { get; private set; }

        public Task<Result<string>> HandleAsync(
            RejectableCommand command,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(Result<string>.Success("unexpected"));
        }
    }

    private sealed class RecordingBehavior<TMessage, TResult>(
        string name,
        IList<string> calls) : IDispatchBehavior<TMessage, TResult>
    {
        public async Task<Result<TResult>> HandleAsync(
            TMessage message,
            DispatchHandlerDelegate<TResult> next,
            CancellationToken cancellationToken)
        {
            calls.Add($"{name}:before");
            var result = await next(cancellationToken);
            calls.Add($"{name}:after");
            return result;
        }
    }

    private sealed class RejectingBehavior<TMessage, TResult>
        : IDispatchBehavior<TMessage, TResult>
    {
        public Task<Result<TResult>> HandleAsync(
            TMessage message,
            DispatchHandlerDelegate<TResult> next,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result<TResult>.Failure(new Error(
                "rejected",
                "Rejected before the handler.",
                ErrorType.Validation)));
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
