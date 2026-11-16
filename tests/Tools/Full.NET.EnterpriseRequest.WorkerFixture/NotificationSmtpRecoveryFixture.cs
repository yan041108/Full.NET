using System.Net.Security;
using System.Security.Cryptography;
using Full.NET.Modules.Notifications.Execution;
using Full.NET.Modules.Notifications.Providers.Smtp;
using MailKit.Net.Smtp;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Full.NET.Host.Worker;

/// <summary>通知恢复演练仅在独立测试进程中信任本次 SMTP 收件箱证书。</summary>
internal static class NotificationSmtpRecoveryFixture
{
    internal static bool TryRegister(IServiceCollection services, IConfiguration configuration, HashSet<ServiceDescriptor> existing)
    {
        var fingerprint = configuration["RecoveryFixture:NotificationSmtpCertificateSha256"];
        if (string.IsNullOrEmpty(fingerprint)) return false;

        foreach (var descriptor in services.Where(item => item.ServiceType == typeof(IHostedService)
            && !existing.Contains(item) && item.ImplementationType != typeof(NotificationDeliveryHostedProcessor)).ToArray())
            services.Remove(descriptor);

        services.RemoveAll<ISmtpMailTransport>();
        services.AddSingleton<ISmtpMailTransport>(new MailKitSmtpTransport(() => new SmtpClient
        {
            ServerCertificateValidationCallback = (_, certificate, _, errors) =>
                certificate is not null
                && (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) == 0
                && string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), fingerprint, StringComparison.Ordinal)
        }));
        return true;
    }
}
