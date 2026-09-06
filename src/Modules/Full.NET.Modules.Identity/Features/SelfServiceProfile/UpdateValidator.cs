using FluentValidation;
using Full.NET.Abstractions.Results;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>校验自助档案更新请求形状。</summary>
internal sealed class UpdateValidator : AbstractValidator<UpdateCommand>
{
    public UpdateValidator()
    {
        RuleFor(command => command.Request)
            .NotNull();
        When(command => command.Request.DisplayName is not null, () =>
        {
            RuleFor(command => command.Request.DisplayName)
                .NotEmpty()
                .MaximumLength(128);
            RuleFor(command => command.Request.UserVersion)
                .NotNull()
                .GreaterThanOrEqualTo(0);
        });
        When(command => command.Request.Profile is not null, () =>
        {
            RuleFor(command => command.Request.Profile!.Version)
                .GreaterThanOrEqualTo(0)
                .When(command => command.Request.Profile!.Version.HasValue);
        });
    }
}
