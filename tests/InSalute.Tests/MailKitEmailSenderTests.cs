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
