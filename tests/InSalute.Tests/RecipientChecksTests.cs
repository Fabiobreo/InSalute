using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class RecipientChecksTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private ApplicationUser _operator = default!;

    public async Task InitializeAsync()
    {
        _operator = await _env.AddUserAsync("operatore", Roles.Operatore);
        _env.CurrentUser.UserId = _operator.Id;
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private async Task AddDispatchAsync(string email, string lastName, bool legacy = false)
    {
        using var scope = _env.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Dispatches.Add(new Dispatch
        {
            Id = Guid.NewGuid(),
            CreatedByUserId = _operator.Id,
            PatientFirstName = legacy ? "" : "Mario",
            PatientLastName = lastName,
            RecipientEmail = email,
            IsLegacyImport = legacy,
        });
        await db.SaveChangesAsync();
    }

    private async Task<bool> UsedForAnotherAsync(string email, string lastName)
    {
        using var scope = _env.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RecipientChecks>().IsUsedForAnotherPatientAsync(email, lastName);
        Assert.True(result.Succeeded);
        return result.Value;
    }

    [Fact]
    public void Typo_in_the_domain_suggests_the_right_one()
    {
        var suggestion = RecipientChecks.SuggestDomain("mario.rossi@gmial.com");

        Assert.NotNull(suggestion);
        Assert.Equal("gmail.com", suggestion.Domain);
        Assert.Equal("mario.rossi@gmail.com", suggestion.Email);
    }

    [Theory]
    [InlineData("mario@gmail.com")]
    [InlineData("mario@GMAIL.COM")]
    [InlineData("mario@studio-medico-rossi.it")]
    public void Correct_or_unrelated_domain_gives_no_warning(string email) =>
        Assert.Null(RecipientChecks.SuggestDomain(email));

    [Theory]
    [InlineData("mario@hotmial.it", "hotmail.it")]
    [InlineData("mario@liberoo.it", "libero.it")]
    [InlineData("mario@gmail.co", "gmail.com")]
    public void Other_typos_are_caught(string email, string expected) =>
        Assert.Equal(expected, RecipientChecks.SuggestDomain(email)?.Domain);

    [Theory]
    [InlineData("mario@gmail.com", true)]
    [InlineData("  mario@gmail.com  ", true)]
    [InlineData("mario@gmail", false)]
    [InlineData("mario", false)]
    [InlineData("", false)]
    [InlineData("Mario Rossi <mario@gmail.com>", false)]
    [InlineData("a@b.it, c@d.it", false)]
    public void Email_format_is_checked(string email, bool valid) =>
        Assert.Equal(valid, RecipientChecks.IsValidEmail(email));

    [Fact]
    public async Task Email_used_before_for_another_last_name_gives_a_warning()
    {
        await AddDispatchAsync("famiglia@example.test", "Rossi");

        Assert.True(await UsedForAnotherAsync("famiglia@example.test", "Bianchi"));
        Assert.True(await UsedForAnotherAsync("  FAMIGLIA@example.test ", "Bianchi"));
    }

    [Fact]
    public async Task Same_last_name_or_unknown_email_gives_no_warning()
    {
        await AddDispatchAsync("famiglia@example.test", "Rossi");

        Assert.False(await UsedForAnotherAsync("famiglia@example.test", "rossi"));
        Assert.False(await UsedForAnotherAsync("famiglia@example.test", "  ROSSI "));
        Assert.False(await UsedForAnotherAsync("altro@example.test", "Bianchi"));
    }

    [Fact]
    public async Task Imported_records_without_last_name_do_not_trigger_the_warning()
    {
        await AddDispatchAsync("famiglia@example.test", "", legacy: true);

        Assert.False(await UsedForAnotherAsync("famiglia@example.test", "Bianchi"));
    }

    [Fact]
    public async Task Warning_requires_a_user_allowed_to_send()
    {
        _env.CurrentUser.UserId = null;
        using var scope = _env.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<RecipientChecks>().IsUsedForAnotherPatientAsync("a@b.it", "Rossi");

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void File_name_check_ignores_case_accents_and_spaces()
    {
        Assert.False(RecipientChecks.NoFileNameContainsLastName("Rossi", ["Analisi_ROSSI_2026.pdf"]));
        Assert.False(RecipientChecks.NoFileNameContainsLastName("De Luca", ["referto-deluca.pdf"]));
        Assert.False(RecipientChecks.NoFileNameContainsLastName("Pérez", ["perez.pdf"]));
        Assert.True(RecipientChecks.NoFileNameContainsLastName("Rossi", ["scan001.pdf", "img.jpg"]));
    }
}

public class DispatchFileNameTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Name_has_the_expected_shape() =>
        Assert.Equal("Referto_Rossi_Mario_20261026.pdf", DispatchFileName.Build("Rossi", "Mario", Now, ".pdf"));

    [Fact]
    public void Accents_are_removed_and_symbols_become_underscores() =>
        Assert.Equal("Referto_D_Angelo_Nicolo_Maria_20261026.zip", DispatchFileName.Build("D'Angelo", "Nicolò  Maria", Now, ".zip"));

    [Fact]
    public void Date_is_the_italian_one() =>
        Assert.EndsWith("_20261027.pdf", DispatchFileName.Build("Rossi", "Mario", new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero), ".pdf"));

    [Fact]
    public void Each_part_is_at_most_60_characters() =>
        Assert.Equal(60, DispatchFileName.Sanitize(new string('a', 100)).Length);

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("a\\b:c*d?e")]
    public void Sanitized_text_only_has_letters_digits_and_underscores(string text) =>
        Assert.Matches("^[A-Za-z0-9_]+$", DispatchFileName.Sanitize(text));

    [Fact]
    public void Empty_text_gets_a_placeholder() =>
        Assert.Equal("Paziente", DispatchFileName.Sanitize("  ***  "));
}
