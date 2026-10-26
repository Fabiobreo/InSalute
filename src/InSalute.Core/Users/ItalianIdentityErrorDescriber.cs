using InSalute.Core.Texts;
using Microsoft.AspNetCore.Identity;

namespace InSalute.Core.Users;

public class ItalianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError DefaultError() => new() { Code = nameof(DefaultError), Description = Messaggi.ErroreGenerico };

    public override IdentityError PasswordTooShort(int length) => new() { Code = nameof(PasswordTooShort), Description = Messaggi.PasswordTroppoCorta };

    public override IdentityError PasswordRequiresDigit() => new() { Code = nameof(PasswordRequiresDigit), Description = Messaggi.PasswordSenzaCifra };

    public override IdentityError PasswordMismatch() => new() { Code = nameof(PasswordMismatch), Description = "La password attuale non è corretta." };

    public override IdentityError DuplicateUserName(string userName) => new() { Code = nameof(DuplicateUserName), Description = Messaggi.UsernameGiaInUso };

    public override IdentityError DuplicateEmail(string email) => new() { Code = nameof(DuplicateEmail), Description = Messaggi.EmailGiaInUso };

    public override IdentityError InvalidEmail(string? email) => new() { Code = nameof(InvalidEmail), Description = Messaggi.EmailNonValida };

    public override IdentityError InvalidUserName(string? userName) => new() { Code = nameof(InvalidUserName), Description = Messaggi.UsernameNonValido };
}
