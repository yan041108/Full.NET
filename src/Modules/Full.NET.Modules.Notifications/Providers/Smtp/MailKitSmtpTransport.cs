using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Full.NET.Modules.Notifications.Providers.Smtp;

internal enum SmtpSecureSocketMode
{
    SslOnConnect,
    StartTls,
}

internal enum SmtpTransportFailureKind
{
    Authentication,
    Permanent,
    Transient,
    RateLimited,
}

internal enum SmtpTransportStage
{
    Connect,
    Authenticate,
    Send,
    Disconnect,
}

/// <summary>SMTP 传输失败的稳定分类；消息不得携带服务器原文或凭据。</summary>
internal sealed class SmtpTransportException(
    SmtpTransportFailureKind failureKind,
    SmtpTransportStage failureStage = SmtpTransportStage.Send,
    Exception? cause = null)
    : Exception("SMTP transport failed.", cause)
{
    public SmtpTransportFailureKind FailureKind { get; } = failureKind;

    public SmtpTransportStage FailureStage { get; } = failureStage;

    public string? SourceExceptionType { get; } = cause?.GetType().Name;
}

/// <summary>一次 SMTP 调用参数；禁止使用 record 自动格式化，避免 Password 进入诊断文本。</summary>
internal sealed class SmtpSendCommand(
    string host,
    int port,
    SmtpSecureSocketMode secureSocketMode,
    string username,
    string password,
    string fromAddress,
    string? fromDisplayName,
    string recipientAddress,
    string subject,
    string body,
    string idempotencyKey)
{
    public string Host { get; } = host;

    public int Port { get; } = port;

    public SmtpSecureSocketMode SecureSocketMode { get; } = secureSocketMode;

    public string Username { get; } = username;

    public string Password { get; } = password;

    public string FromAddress { get; } = fromAddress;

    public string? FromDisplayName { get; } = fromDisplayName;

    public string RecipientAddress { get; } = recipientAddress;

    public string Subject { get; } = subject;

    public string Body { get; } = body;

    public string IdempotencyKey { get; } = idempotencyKey;

    public override string ToString() =>
        $"SmtpSendCommand {{ Host = {Host}, Port = {Port}, SecureSocketMode = {SecureSocketMode}, Password = [redacted] }}";
}

internal interface ISmtpMailTransport
{
    ValueTask<string> SendAsync(
        SmtpSendCommand command,
        CancellationToken cancellationToken);
}

/// <summary>使用 MailKit 建立一次显式 TLS 连接；不启用协议日志或证书绕过。</summary>
internal sealed class MailKitSmtpTransport : ISmtpMailTransport
{
    public async ValueTask<string> SendAsync(
        SmtpSendCommand command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messageId = CreateMessageId(command.IdempotencyKey);
        var message = new MimeMessage
        {
            MessageId = messageId,
            Subject = command.Subject,
            Body = new TextPart("plain") { Text = command.Body },
        };
        message.From.Add(new MailboxAddress(command.FromDisplayName ?? string.Empty, command.FromAddress));
        message.To.Add(MailboxAddress.Parse(command.RecipientAddress));

        using var client = new SmtpClient();
        var stage = SmtpTransportStage.Connect;
        try
        {
            var socketOptions = command.SecureSocketMode switch
            {
                SmtpSecureSocketMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
                SmtpSecureSocketMode.StartTls => SecureSocketOptions.StartTls,
                _ => throw new SmtpTransportException(SmtpTransportFailureKind.Permanent),
            };
            await client.ConnectAsync(
                    command.Host,
                    command.Port,
                    socketOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            stage = SmtpTransportStage.Authenticate;
            await client.AuthenticateAsync(command.Username, command.Password, cancellationToken)
                .ConfigureAwait(false);
            stage = SmtpTransportStage.Send;
            await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            stage = SmtpTransportStage.Disconnect;
            await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
            return messageId;
        }
        catch (Exception) when (IsMessageAlreadyAccepted(stage))
        {
            // DATA 已被服务器接受后，断开失败不能把本次投递改回可重试，否则会制造重复邮件。
            return messageId;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (MailKit.Security.AuthenticationException exception)
        {
            throw new SmtpTransportException(
                SmtpTransportFailureKind.Authentication,
                stage,
                exception);
        }
        catch (SmtpCommandException exception)
        {
            var statusCode = (int)exception.StatusCode;
            throw new SmtpTransportException(
                statusCode is >= 400 and <= 499
                    ? SmtpTransportFailureKind.Transient
                    : SmtpTransportFailureKind.Permanent,
                stage,
                exception);
        }
        catch (SmtpProtocolException exception)
        {
            throw new SmtpTransportException(
                ClassifyProtocolFailure(stage),
                stage,
                exception);
        }
        catch (Exception exception) when (
            exception is IOException
                or SocketException
                or System.Security.Authentication.AuthenticationException)
        {
            throw new SmtpTransportException(
                SmtpTransportFailureKind.Transient,
                stage,
                exception);
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or FormatException
                or InvalidOperationException
                or NotSupportedException)
        {
            throw new SmtpTransportException(
                SmtpTransportFailureKind.Permanent,
                stage,
                exception);
        }
    }

    internal static SmtpTransportFailureKind ClassifyProtocolFailure(
        SmtpTransportStage stage) =>
        stage == SmtpTransportStage.Authenticate
            ? SmtpTransportFailureKind.Authentication
            : SmtpTransportFailureKind.Transient;

    internal static bool IsMessageAlreadyAccepted(SmtpTransportStage stage) =>
        stage == SmtpTransportStage.Disconnect;

    private static string CreateMessageId(string idempotencyKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return $"fullnet-{Convert.ToHexString(hash).ToLowerInvariant()}@local.invalid";
    }
}
