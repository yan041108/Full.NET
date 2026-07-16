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

        var errors = failures
            .GroupBy(failure => failure.PropertyName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(failure => failure.ErrorMessage)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        return Result<TResult>.Failure(new Error(
            "validation.failed",
            "One or more validation errors occurred.",
            ErrorType.Validation,
            errors));
    }
}
