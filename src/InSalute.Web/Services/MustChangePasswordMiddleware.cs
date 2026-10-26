namespace InSalute.Web.Services;

/// <summary>Finché la password temporanea non è stata cambiata, l'unica pagina raggiungibile è il cambio password.</summary>
internal sealed class MustChangePasswordMiddleware(RequestDelegate next)
{
    private const string ChangePasswordPath = "/Account/Manage/ChangePassword";

    private static readonly string[] AllowedPrefixes =
        [ChangePasswordPath, "/Account/Logout", "/_blazor", "/_framework", "/_content"];

    public Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        if (context.User.HasClaim(c => c.Type == AppClaims.MustChangePassword)
            && !Path.HasExtension(path.Value)
            && !AllowedPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.Redirect(ChangePasswordPath);
            return Task.CompletedTask;
        }

        return next(context);
    }
}
