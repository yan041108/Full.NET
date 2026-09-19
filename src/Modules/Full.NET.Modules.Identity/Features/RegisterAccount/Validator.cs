using FluentValidation;

namespace Full.NET.Modules.Identity.Features.RegisterAccount;

internal sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(command => command.Request.Email).NotEmpty();
        RuleFor(command => command.Request.DisplayName).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Request.Password).NotEmpty();
        RuleFor(command => command.Request.ChallengeId).NotEmpty();
        RuleFor(command => command.Request.ChallengeCode).NotEmpty();
    }
}
