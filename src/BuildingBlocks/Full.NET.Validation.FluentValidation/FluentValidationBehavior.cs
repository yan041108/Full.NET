using FluentValidation;
using FluentValidation.Results;
using Full.NET.Abstractions.Messaging;
using Full.NET.Abstractions.Results;

namespace Full.NET.Validation.FluentValidation;

internal sealed class FluentValidationBehavior<TMessage, TResult>(
    IEnumerable<IValidator<TMessage>> validators)
    : IDispatchBehavior<TMessage, TResult>
{
    public async Task<Result<TResult>> HandleAsync(
        TMessage message,
        DispatchHandlerDelegate<TResult> next,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(
                new ValidationContext<TMessage>(message),
                cancellationToken);
            failures.AddRange(result.Errors.Where(failure =>
                !string.IsNullOrWhiteSpace(failure.ErrorMessage)));
        }

        if (failures.Count == 0)
        {
            return await next(cancellationToken);
        }

        var normalizedFailures = failures
            .Select(failure => new NormalizedFailure(
                failure.PropertyName,
                NormalizeErrorCode(failure.ErrorCode),
                failure.ErrorMessage,
                ExtractArguments(failure)))
            .DistinctBy(
                failure => new
                {
                    failure.Field,
                    failure.Code,
                    failure.DefaultMessage,
                })
            .ToArray();

        var errors = normalizedFailures
            .GroupBy(failure => failure.Field, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.DefaultMessage)
                    .ToArray(),
                StringComparer.Ordinal);
        var violations = normalizedFailures
            .Select(failure => new ValidationViolation(
                failure.Field,
                failure.Code,
                failure.Arguments))
            .ToArray();

        return Result<TResult>.Failure(new Error(
            Code: ValidationErrorCodes.Failed,
            DefaultMessage: "One or more validation errors occurred.",
            Type: ErrorType.Validation,
            ValidationErrors: errors,
            ValidationViolations: violations));
    }

    private static string NormalizeErrorCode(string? code) =>
        !string.IsNullOrWhiteSpace(code)
        && code.StartsWith(ValidationErrorCodes.Prefix, StringComparison.Ordinal)
            ? code
            : ValidationErrorCodes.InvalidFormat;

    private static IReadOnlyDictionary<string, object?> ExtractArguments(
        ValidationFailure failure)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);
        CopyAllowedArgument(failure, arguments, "MinLength");
        CopyAllowedArgument(failure, arguments, "MaxLength");
        CopyAllowedArgument(failure, arguments, "From");
        CopyAllowedArgument(failure, arguments, "To");
        return arguments;
    }

    private static void CopyAllowedArgument(
        ValidationFailure failure,
        IDictionary<string, object?> destination,
        string name)
    {
        if (failure.FormattedMessagePlaceholderValues is not null
            && failure.FormattedMessagePlaceholderValues.TryGetValue(name, out var value)
            && value is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal)
        {
            destination[name] = value;
        }
    }

    private sealed record NormalizedFailure(
        string Field,
        string Code,
        string DefaultMessage,
        IReadOnlyDictionary<string, object?> Arguments);
}
