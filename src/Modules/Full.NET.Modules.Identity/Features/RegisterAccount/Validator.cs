using FluentValidation;

/// <summary>FluentValidation 规则；只校验输入形状，不访问数据库。</summary>
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
