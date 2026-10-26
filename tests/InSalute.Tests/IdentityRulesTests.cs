using InSalute.Core.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class IdentityRulesTests
{
    [Theory]
    [InlineData("corta1", false)]
    [InlineData("soloLettereLunga", false)]
    [InlineData("1234567890123", false)]
    [InlineData("password1234", true)]
    [InlineData("PASSWORD1234", true)]
    public async Task Password_policy_requires_10_chars_one_letter_and_one_digit(string password, bool accepted)
    {
        await using var env = new TestEnvironment();
        using var scope = env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var result = await users.CreateAsync(
            new ApplicationUser { UserName = "tizio", Email = "tizio@example.test", DisplayName = "Tizio" }, password);

        Assert.Equal(accepted, result.Succeeded);
    }

    [Fact]
    public async Task Account_is_locked_for_15_minutes_after_five_failed_attempts()
    {
        await using var env = new TestEnvironment();
        var user = await env.AddUserAsync("op", Roles.Operatore);

        using var scope = env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var tracked = (await users.FindByIdAsync(user.Id))!;
        for (var i = 0; i < 5; i++)
        {
            await users.AccessFailedAsync(tracked);
        }

        Assert.True(await users.IsLockedOutAsync(tracked));
        var remaining = (await users.GetLockoutEndDateAsync(tracked))!.Value - DateTimeOffset.UtcNow;
        Assert.InRange(remaining, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Email_must_be_unique()
    {
        await using var env = new TestEnvironment();
        await env.AddUserAsync("op", Roles.Operatore);
        using var scope = env.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var duplicate = await users.CreateAsync(
            new ApplicationUser { UserName = "altro", Email = "op@example.test", DisplayName = "Altro" }, TestEnvironment.DefaultPassword);

        Assert.False(duplicate.Succeeded);
    }
}
