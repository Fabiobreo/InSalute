using InSalute.Core.Texts;
using Microsoft.AspNetCore.Identity;

namespace InSalute.Core.Users;

/// <summary>Identity non ha un'opzione "almeno una lettera" (le sue sono maiuscola/minuscola separate).</summary>
public class LetterPasswordValidator<TUser> : IPasswordValidator<TUser> where TUser : class
{
    public Task<IdentityResult> ValidateAsync(UserManager<TUser> manager, TUser user, string? password)
    {
        if (password is not null && !password.Any(char.IsLetter))
        {
            return Task.FromResult(IdentityResult.Failed(
                new IdentityError { Code = "PasswordRequiresLetter", Description = Messaggi.PasswordSenzaLettera }));
        }
        return Task.FromResult(IdentityResult.Success);
    }
}
