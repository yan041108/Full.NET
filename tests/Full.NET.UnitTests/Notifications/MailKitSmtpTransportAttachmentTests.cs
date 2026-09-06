using Full.NET.Modules.Notifications.Providers;
using Full.NET.Modules.Notifications.Providers.Smtp;
using MimeKit;

namespace Full.NET.UnitTests.Notifications;

[TestClass]
public sealed class MailKitSmtpTransportAttachmentTests
{
    [TestMethod]
    public async Task SendAsync_builds_multipart_message_when_attachments_present()
    {
        var transport = new CapturingSmtpTransport();
        var command = new SmtpSendCommand(
            "smtp.example.com",
            465,
            SmtpSecureSocketMode.SslOnConnect,
            "sender@example.com",
            "secret",
            "sender@example.com",
            "Full.NET",
            "recipient@example.com",
            "Subject",
            "Body",
            "idempotency-key",
            [new NotificationProviderAttachment("report.pdf", "application/pdf", [1, 2, 3])]);

        await transport.SendAsync(command, CancellationToken.None);

        Assert.IsNotNull(transport.Message);
        Assert.IsInstanceOfType(transport.Message!.Body, typeof(Multipart));
        var multipart = (Multipart)transport.Message.Body;
        Assert.AreEqual(2, multipart.Count);
        Assert.IsInstanceOfType(multipart[0], typeof(TextPart));
        Assert.IsInstanceOfType(multipart[1], typeof(MimePart));
    }

    private sealed class CapturingSmtpTransport : ISmtpMailTransport
    {
        public MimeMessage? Message { get; private set; }

        public ValueTask<string> SendAsync(
            SmtpSendCommand command,
            CancellationToken cancellationToken)
        {
            Message = new MimeMessage
            {
                Subject = command.Subject,
                Body = BuildBody(command),
            };
            return ValueTask.FromResult("message-id");
        }

        private static MimeEntity BuildBody(SmtpSendCommand command)
        {
            var textPart = new TextPart("plain") { Text = command.Body };
            if (command.Attachments.Count == 0)
            {
                return textPart;
            }

            var mixed = new Multipart("mixed") { textPart };
            foreach (var attachment in command.Attachments)
            {
                mixed.Add(new MimePart(attachment.ContentType)
                {
                    Content = new MimeContent(new MemoryStream(attachment.Content, writable: false)),
                    FileName = attachment.FileName,
                });
            }

            return mixed;
        }
    }
}
