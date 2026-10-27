using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Texts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace InSalute.Tests;

public class DispatchHistoryServiceTests : IAsyncLifetime
{
    private readonly TestEnvironment _env = new();
    private ApplicationUser _operator = default!;
    private ApplicationUser _otherOperator = default!;
    private ApplicationUser _admin = default!;
    private ApplicationUser _manager = default!;
    private ApplicationUser _super = default!;

    public async Task InitializeAsync()
    {
        _operator = await _env.AddUserAsync("operatore", Roles.Operatore);
        _otherOperator = await _env.AddUserAsync("altro", Roles.Operatore);
        _manager = await _env.AddUserAsync("manager", Roles.Manager);
        _admin = await _env.AddUserAsync("admin", Roles.Admin);
        _super = await _env.AddUserAsync("assistenza", Roles.SuperAdmin);
    }

    public async Task DisposeAsync() => await _env.DisposeAsync();

    private async Task<Dispatch> AddAsync(
        ApplicationUser by, string last = "Rossi", string email = "rossi@example.test",
        DateTime? at = null, DispatchStepStatus report = DispatchStepStatus.Sent,
        DispatchStepStatus password = DispatchStepStatus.Sent, bool legacy = false)
    {
        using var scope = _env.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatch = new Dispatch
        {
            Id = Guid.NewGuid(),
            CreatedAt = at ?? DateTime.UtcNow,
            CreatedByUserId = by.Id,
            PatientFirstName = legacy ? "" : "Mario",
            PatientLastName = legacy ? "" : last,
            RecipientEmail = email,
            AttachmentsJson = """[{"fileName":"a.pdf","sizeBytes":10,"sha256":"abc"}]""",
            ReportStatus = report,
            PasswordStatus = password,
            IsLegacyImport = legacy,
        };
        db.Dispatches.Add(dispatch);
        await db.SaveChangesAsync();
        return dispatch;
    }

    private async Task<T> AsAsync<T>(ApplicationUser user, Func<DispatchHistoryService, Task<T>> action)
    {
        _env.CurrentUser.UserId = user.Id;
        using var scope = _env.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<DispatchHistoryService>());
    }

    private async Task<DispatchPage> QueryAsync(ApplicationUser user, DispatchFilter? filter = null, int page = 0, int size = 25)
    {
        var result = await AsAsync(user, s => s.QueryAsync(filter ?? new DispatchFilter(), page, size));
        Assert.True(result.Succeeded, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task Every_role_sees_every_dispatch_newest_first()
    {
        await AddAsync(_operator, "Vecchio", at: DateTime.UtcNow.AddDays(-2));
        await AddAsync(_otherOperator, "Nuovo", at: DateTime.UtcNow);

        foreach (var user in new[] { _operator, _manager, _admin, _super })
        {
            var page = await QueryAsync(user);
            Assert.Equal(2, page.TotalCount);
            Assert.StartsWith("Nuovo", page.Items[0].Patient);
        }
    }

    [Fact]
    public async Task Anonymous_callers_are_refused()
    {
        _env.CurrentUser.UserId = null;
        using var scope = _env.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<DispatchHistoryService>();

        Assert.False((await service.QueryAsync(new DispatchFilter(), 0, 25)).Succeeded);
        Assert.False((await service.GetAsync(Guid.NewGuid())).Succeeded);
        Assert.False((await service.GetOperatorsAsync()).Succeeded);
    }

    [Fact]
    public async Task Super_admin_dispatches_show_as_technical_support_to_everyone_but_a_super_admin()
    {
        var sent = await AddAsync(_super, "Bianchi");

        foreach (var user in new[] { _operator, _manager, _admin })
        {
            var item = Assert.Single((await QueryAsync(user)).Items);
            Assert.Equal(DispatchHistoryService.TechnicalSupportName, item.OperatorName);
            var detail = (await AsAsync(user, s => s.GetAsync(sent.Id))).Value!;
            Assert.Equal(DispatchHistoryService.TechnicalSupportName, detail.OperatorName);
        }

        var own = Assert.Single((await QueryAsync(_super)).Items);
        Assert.Equal(_super.DisplayName, own.OperatorName);
    }

    [Fact]
    public async Task Super_admin_is_not_in_the_operator_filter_unless_the_caller_is_one()
    {
        var forAdmin = (await AsAsync(_admin, s => s.GetOperatorsAsync())).Value!;
        Assert.DoesNotContain(forAdmin, o => o.Id == _super.Id);
        Assert.Contains(forAdmin, o => o.Id == _operator.Id);

        var forSuper = (await AsAsync(_super, s => s.GetOperatorsAsync())).Value!;
        Assert.Contains(forSuper, o => o.Id == _super.Id);
    }

    [Fact]
    public async Task Filtering_by_a_super_admin_id_reveals_nothing_to_other_roles()
    {
        await AddAsync(_super);

        Assert.Empty((await QueryAsync(_admin, new DispatchFilter(OperatorId: _super.Id))).Items);
        Assert.Single((await QueryAsync(_super, new DispatchFilter(OperatorId: _super.Id))).Items);
    }

    [Fact]
    public async Task Overall_status_follows_the_report_and_password_steps()
    {
        await AddAsync(_operator, "Ok");
        await AddAsync(_operator, "SenzaPassword", password: DispatchStepStatus.Abandoned);
        await AddAsync(_operator, "NonInviato", report: DispatchStepStatus.Failed, password: DispatchStepStatus.Pending);
        await AddAsync(_operator, legacy: true, report: DispatchStepStatus.Unknown, password: DispatchStepStatus.Unknown);

        var all = (await QueryAsync(_admin)).Items.ToDictionary(i => i.Patient.Split(' ')[0], i => i.Status);
        Assert.Equal(DispatchOverallStatus.Completed, all["Ok"]);
        Assert.Equal(DispatchOverallStatus.PasswordNotSent, all["SenzaPassword"]);
        Assert.Equal(DispatchOverallStatus.NotSent, all["NonInviato"]);
        Assert.Equal(DispatchOverallStatus.Imported, all["—"]);

        foreach (var status in Enum.GetValues<DispatchOverallStatus>())
        {
            var filtered = await QueryAsync(_admin, new DispatchFilter(Status: status));
            Assert.Single(filtered.Items);
            Assert.Equal(status, filtered.Items[0].Status);
        }
    }

    [Fact]
    public async Task Filters_by_date_text_and_operator()
    {
        await AddAsync(_operator, "Rossi", "a@example.test", at: new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc));
        await AddAsync(_otherOperator, "Verdi", "verdi@example.test", at: new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc));

        var byDate = await QueryAsync(_admin, new DispatchFilter(From: new DateOnly(2026, 3, 15), To: new DateOnly(2026, 3, 20)));
        Assert.Equal("Verdi Mario", Assert.Single(byDate.Items).Patient);

        // Il giorno finale è incluso per intero.
        var lastDay = await QueryAsync(_admin, new DispatchFilter(To: new DateOnly(2026, 3, 10)));
        Assert.Single(lastDay.Items);

        Assert.Single((await QueryAsync(_admin, new DispatchFilter(Text: "VERDI"))).Items);
        Assert.Single((await QueryAsync(_admin, new DispatchFilter(Text: "a@example"))).Items);
        Assert.Equal("Rossi Mario", Assert.Single((await QueryAsync(_admin, new DispatchFilter(OperatorId: _operator.Id))).Items).Patient);
    }

    [Fact]
    public async Task Paging_is_done_on_the_server()
    {
        for (var i = 0; i < 30; i++)
        {
            await AddAsync(_operator, $"P{i:00}", at: DateTime.UtcNow.AddMinutes(-i));
        }

        var first = await QueryAsync(_admin, page: 0, size: 25);
        var second = await QueryAsync(_admin, page: 1, size: 25);

        Assert.Equal(30, first.TotalCount);
        Assert.Equal(25, first.Items.Count);
        Assert.Equal(5, second.Items.Count);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Fact]
    public async Task Detail_returns_files_and_errors_and_unknown_ids_fail()
    {
        var d = await AddAsync(_operator, report: DispatchStepStatus.Failed, password: DispatchStepStatus.Pending);

        var detail = (await AsAsync(_manager, s => s.GetAsync(d.Id))).Value!;
        var file = Assert.Single(detail.Files);
        Assert.Equal(("a.pdf", 10L, "abc"), (file.FileName, file.SizeBytes, file.Sha256));
        Assert.Equal(DispatchOverallStatus.NotSent, detail.Status);

        var missing = await AsAsync(_manager, s => s.GetAsync(Guid.NewGuid()));
        Assert.False(missing.Succeeded);
        Assert.Equal(Messaggi.InvioNonTrovato, missing.Error);
    }
}
