using FluentValidation;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;
using Full.NET.Modularity.Messaging;
using Full.NET.Validation.FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Full.NET.UnitTests.Validation;

[TestClass]
public sealed class FluentValidationBehaviorTests
{
    [TestMethod]
    public async Task No_validators_invokes_handler()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler).BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<TestCommand, string>(new TestCommand("value"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("value", result.Value);
        Assert.IsTrue(handler.Executed);
    }

    [TestMethod]
    public async Task Validation_failure_returns_structured_error_and_skips_handler()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler)
            .AddSingleton<IValidator<TestCommand>>(new RequiredValidator())
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<TestCommand, string>(new TestCommand(string.Empty));

        Assert.IsFalse(result.IsSuccess);
        var error = result.Error;
        Assert.IsNotNull(error);
        Assert.AreEqual(ValidationErrorCodes.Failed, error.Code);
        Assert.AreEqual(
            "One or more validation errors occurred.",
            error.DefaultMessage);
        Assert.AreEqual(ErrorType.Validation, error.Type);
        Assert.IsNotNull(error.ValidationErrors);
        CollectionAssert.AreEqual(
            new[] { "Value is required." },
            error.ValidationErrors[nameof(TestCommand.Value)]);
        Assert.IsNotNull(error.ValidationViolations);
        Assert.HasCount(1, error.ValidationViolations);
        Assert.AreEqual(
            nameof(TestCommand.Value),
            error.ValidationViolations[0].Field);
        Assert.AreEqual(
            ValidationErrorCodes.Required,
            error.ValidationViolations[0].Code);
        Assert.HasCount(0, error.ValidationViolations[0].Arguments);
        Assert.IsFalse(handler.Executed);
    }

    [TestMethod]
    public async Task Allowed_length_argument_is_exposed_without_validated_value()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler)
            .AddSingleton<IValidator<TestCommand>>(new MaximumLengthValidator())
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<TestCommand, string>(new TestCommand("toolong"));

        var violation = result.Error?.ValidationViolations?.Single();
        Assert.IsNotNull(violation);
        Assert.AreEqual(ValidationErrorCodes.MaximumLength, violation.Code);
        Assert.AreEqual(3, violation.Arguments["MaxLength"]);
        Assert.IsFalse(violation.Arguments.ContainsKey("PropertyValue"));
    }

    [TestMethod]
    public async Task Multiple_validators_merge_and_deduplicate_messages()
    {
        var handler = new RecordingHandler();
        await using var provider = CreateServices(handler)
            .AddSingleton<IValidator<TestCommand>>(new RequiredValidator())
            .AddSingleton<IValidator<TestCommand>>(new FormatValidator())
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandDispatcher>()
            .SendAsync<TestCommand, string>(new TestCommand(string.Empty));

        Assert.IsFalse(result.IsSuccess);
        var validationErrors = result.Error?.ValidationErrors;
        Assert.IsNotNull(validationErrors);
        CollectionAssert.AreEqual(
            new[] { "Value is required.", "Value has an invalid format." },
            validationErrors[nameof(TestCommand.Value)]);
        var violations = result.Error?.ValidationViolations;
        Assert.IsNotNull(violations);
        Assert.HasCount(2, violations);
        Assert.IsFalse(handler.Executed);
    }

    [TestMethod]
    public void Registration_is_idempotent()
    {
        var services = new ServiceCollection();

        services.AddFullNetFluentValidation();
        services.AddFullNetFluentValidation();

        var registrations = services
            .Where(item => item.ServiceType == typeof(IDispatchBehavior<,>))
            .ToArray();
        Assert.AreEqual(1, registrations.Length);
        Assert.AreEqual(
            "FluentValidationBehavior`2",
            registrations[0].ImplementationType?.Name);
    }

    [TestMethod]
    public void Closed_registration_is_idempotent_and_does_not_add_open_behavior()
    {
        var services = new ServiceCollection();

        services.AddFullNetFluentValidation<TestCommand, string>();
        services.AddFullNetFluentValidation<TestCommand, string>();

        var registrations = services
            .Where(item => item.ServiceType == typeof(IDispatchBehavior<TestCommand, string>))
            .ToArray();
        Assert.HasCount(1, registrations);
        Assert.IsFalse(services.Any(item =>
            item.ServiceType == typeof(IDispatchBehavior<,>)));
    }

    private static IServiceCollection CreateServices(RecordingHandler handler) =>
        new ServiceCollection()
            .AddFullNetFluentValidation()
            .AddSingleton<ICommandHandler<TestCommand, string>>(handler)
            .AddScoped<ICommandDispatcher, CommandDispatcher>();

    private sealed record TestCommand(string Value) : ICommand<string>;

    private sealed class RecordingHandler : ICommandHandler<TestCommand, string>
    {
        public bool Executed { get; private set; }

        public Task<Result<string>> HandleAsync(
            TestCommand command,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(Result<string>.Success(command.Value));
        }
    }

    private sealed class RequiredValidator : AbstractValidator<TestCommand>
    {
        public RequiredValidator()
        {
            RuleFor(command => command.Value)
                .NotEmpty()
                .WithErrorCode(ValidationErrorCodes.Required)
                .WithMessage("Value is required.");
        }
    }

    private sealed class MaximumLengthValidator : AbstractValidator<TestCommand>
    {
        public MaximumLengthValidator()
        {
            RuleFor(command => command.Value)
                .MaximumLength(3)
                .WithErrorCode(ValidationErrorCodes.MaximumLength)
                .WithMessage("Value must not exceed {MaxLength} characters.");
        }
    }

    private sealed class FormatValidator : AbstractValidator<TestCommand>
    {
        public FormatValidator()
        {
            RuleFor(command => command.Value)
                .NotEmpty()
                .WithErrorCode(ValidationErrorCodes.Required)
                .WithMessage("Value is required.");
            RuleFor(command => command.Value)
                .Must(_ => false)
                .WithErrorCode(ValidationErrorCodes.InvalidFormat)
                .WithMessage("Value has an invalid format.");
        }
    }
}
