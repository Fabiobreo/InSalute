using InSalute.Core.Data;
using InSalute.Core.Domain;
using InSalute.Core.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace InSalute.Core.Users;

public static class IdentityServiceCollectionExtensions
{
    /// <summary>Identity con le regole di 5.3 e i servizi utenti. L'host aggiunge SignInManager e il resto dell'interfaccia.</summary>
    public static IdentityBuilder AddInSaluteIdentity(this IServiceCollection services)
    {
        services.AddScoped<UserAdminService>();
        services.AddScoped<IdentitySeeder>();

        return services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
                options.User.RequireUniqueEmail = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddErrorDescriber<ItalianIdentityErrorDescriber>()
            .AddPasswordValidator<LetterPasswordValidator<ApplicationUser>>();
    }
}
