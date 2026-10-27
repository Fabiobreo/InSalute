using System.Net.Sockets;
using InSalute.Core.Data;
using InSalute.Core.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class MailKitEmailSenderTests
{
    public static TheoryData<Exception, SendErrorCode> Mappings => new()
    {
        { new MailKit.Security.AuthenticationException("535 credenziali errate"), SendErrorCode.AuthenticationFailed },
        { new SocketException(), SendErrorCode.ConnectionFailed },
        { new SslHandshakeException("handshake", new Exception("inner")), SendErrorCode.ConnectionFailed },
        { new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550 rifiutato"), SendErrorCode.RecipientRejected },
        { new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.ExceededStorageAllocation, "552 troppo grande"), SendErrorCode.MessageTooLarge },
        { new OperationCanceledException(), SendErrorCode.Timeout },
        { new TimeoutException(), SendErrorCode.Timeout },
        { new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.TransactionFailed, "554"), SendErrorCode.Unknown },
        { new InvalidOperationException("altro"), SendErrorCode.Unknown },
    };

    [Theory]
    [MemberData(nameof(Mappings))]
    public void Exceptions_are_mapped_to_error_codes(Exception exception, SendErrorCode expected)
    {
        var result = MailKitEmailSender.MapException(exception);

        Assert.False(result.Success);
        Assert.Equal(expected, result.ErrorCode);
        Assert.False(string.IsNullOrEmpty(result.TechnicalDetails));
    }

    [Fact]
    public void Security_modes_map_to_encrypted_socket_options_only()
    {
        Assert.Equal(SecureSocketOptions.SslOnConnect, MailKitEmailSender.ToSocketOptions(InSalute.Core.Domain.SmtpSecurity.SslOnConnect));
        Assert.Equal(SecureSocketOptions.StartTls, MailKitEmailSender.ToSocketOptions(InSalute.Core.Domain.SmtpSecurity.StartTls));
        Assert.Throws<ArgumentOutOfRangeException>(() => MailKitEmailSender.ToSocketOptions((InSalute.Core.Domain.SmtpSecurity)0));
    }

    private static readonly InSalute.Core.Domain.SmtpSettings Settings = new()
    {
        Host = "smtp.example.test",
        Port = 465,
        Security = InSalute.Core.Domain.SmtpSecurity.SslOnConnect,
        UserName = "info@example.test",
        FromAddress = "info@example.test",
    };

    private static MimeKit.MimeMessage Message() => new(
        [new MimeKit.MailboxAddress("", "info@example.test")],
        [new MimeKit.MailboxAddress("", "dest@example.test")],
        "Oggetto",
        new MimeKit.TextPart("plain") { Text = "Corpo" });

    /// <summary>SmtpClient senza rete: registra le chiamate e lancia dove richiesto.</summary>
    private sealed class FakeSmtpClient : SmtpClient
    {
        public List<string> Calls { get; } = [];

        public Exception? ThrowOnSend { get; init; }

        public Exception? ThrowOnDisconnect { get; init; }

        public override Task ConnectAsync(string host, int port = 0, SecureSocketOptions options = SecureSocketOptions.Auto, CancellationToken cancellationToken = default)
        {
            Calls.Add("connect");
            return Task.CompletedTask;
        }

        public override Task AuthenticateAsync(System.Text.Encoding encoding, System.Net.ICredentials credentials, CancellationToken cancellationToken = default)
        {
            Calls.Add("auth");
            return Task.CompletedTask;
        }

        public override Task<string> SendAsync(MimeKit.FormatOptions options, MimeKit.MimeMessage message, CancellationToken cancellationToken = default, ITransferProgress? progress = null)
        {
            Calls.Add("send");
            return ThrowOnSend is null ? Task.FromResult("250 OK") : Task.FromException<string>(ThrowOnSend);
        }

        public override Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
        {
            Calls.Add("quit");
            return ThrowOnDisconnect is null ? Task.CompletedTask : Task.FromException(ThrowOnDisconnect);
        }
    }

    [Fact]
    public async Task A_failed_quit_after_the_message_was_accepted_is_still_a_success()
    {
        using var client = new FakeSmtpClient { ThrowOnDisconnect = new IOException("connessione chiusa dal server") };

        var result = await MailKitEmailSender.DeliverAsync(client, Settings, "password", Message(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["connect", "auth", "send", "quit"], client.Calls);
    }

    [Fact]
    public async Task A_message_rejected_by_the_server_is_not_a_success()
    {
        using var client = new FakeSmtpClient
        {
            ThrowOnSend = new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "550"),
        };

        await Assert.ThrowsAsync<SmtpCommandException>(
            () => MailKitEmailSender.DeliverAsync(client, Settings, "password", Message(), CancellationToken.None));
        Assert.DoesNotContain("quit", client.Calls);
    }

    [Fact]
    public async Task Without_a_password_there_is_no_authentication()
    {
        using var client = new FakeSmtpClient();

        var result = await MailKitEmailSender.DeliverAsync(client, Settings, password: null, Message(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["connect", "send", "quit"], client.Calls);
    }

    [Fact]
    public async Task Without_saved_settings_it_returns_a_result_instead_of_throwing()
    {
        await using var env = new TestEnvironment();
        using var scope = env.CreateScope();
        var sender = new MailKitEmailSender(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());

        var result = await sender.SendAsync(new EmailMessage("dest@example.test", "Oggetto", "Corpo"));

        Assert.False(result.Success);
        Assert.Equal(SendErrorCode.Unknown, result.ErrorCode);
    }
}
