using Full.NET.Modules.Cryptography.Configuration;
using Full.NET.Modules.Cryptography.Contracts;
using Microsoft.Extensions.Options;

namespace Full.NET.Modules.Cryptography.Features.ManageGmKeys;

/// <summary>读取国密部署状态。</summary>
internal sealed class CryptographyStatusService(IOptions<CryptographyOptions> options)
{
    /// <summary>返回国密控制面部署快照。</summary>
    public CryptographyStatusResponse GetStatus()
    {
        var value = options.Value;
        return new CryptographyStatusResponse(
            "sm2",
            value.DefaultUserId,
            value.SigningPurpose,
            value.DeploymentNotice);
    }
}
