using System.Globalization;
using InSalute.Core;
using InSalute.Core.Data;
using InSalute.Core.Dispatching;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using InSalute.Core.Users;
using InSalute.Web.Components;
using InSalute.Web.Components.Account;
using InSalute.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MudBlazor;
using MudBlazor.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Servizio Windows con Kestrel (non IIS): no-op quando si avvia da riga di comando.
builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

var italian = new CultureInfo("it-IT");
CultureInfo.DefaultThreadCurrentCulture = italian;
CultureInfo.DefaultThreadCurrentUICulture = italian;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices(config => config.SnackbarConfiguration.VisibleStateDuration = 4000);
builder.Services.AddTransient<MudLocalizer, ItalianMudLocalizer>();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
});

// Un utente disattivato o con ruolo cambiato perde l'accesso entro un minuto.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
    options.ValidationInterval = TimeSpan.FromMinutes(1));

var authorization = builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
foreach (var (policy, roles) in Policies.RolesByPolicy)
{
    authorization.AddPolicy(policy, p => p.RequireRole(roles));
}

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' not found.");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddInSaluteIdentity()
    .AddSignInManager()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// Chiavi su disco, protette con DPAPI: servono anche a cifrare la password SMTP salvata nel database.
// La cartella va inclusa nel backup (vedi docs/piano-rifacimento.md, sezione 9).
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("InSalute")
    .PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["Storage:KeysPath"] ?? @"C:\InSaluteData\keys"));
if (OperatingSystem.IsWindows())
{
    dataProtection.ProtectKeysWithDpapi();
}

builder.Services.AddInSaluteSettings();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
builder.Services.AddInSaluteDispatching();
builder.Services.AddHostedService<TempCleanupHostedService>();

var app = builder.Build();

app.UseRequestLocalization(new RequestLocalizationOptions()
    .SetDefaultCulture("it-IT")
    .AddSupportedCultures("it-IT")
    .AddSupportedUICultures("it-IT"));

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<MustChangePasswordMiddleware>();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapAdditionalIdentityEndpoints();

await using (var scope = app.Services.CreateAsyncScope())
{
    var bootstrap = builder.Configuration.GetSection("Bootstrap").Get<BootstrapOptions>() ?? new BootstrapOptions();
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync(bootstrap);
}

app.Run();
