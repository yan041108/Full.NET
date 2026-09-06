using System.Security.Claims;
using Full.NET.Abstractions.Messaging;
using Full.NET.Modules.Identity.Contracts;

namespace Full.NET.Modules.Identity.Features.SelfServiceProfile;

/// <summary>当前用户自助档案更新命令。</summary>
/// <param name="Request">客户端提交的档案补丁。</param>
/// <param name="Principal">当前认证主体。</param>
internal sealed record UpdateCommand(
    UpdateSelfServiceProfileRequest Request,
    ClaimsPrincipal Principal)
    : ICommand<SelfServiceProfileResponse>;
