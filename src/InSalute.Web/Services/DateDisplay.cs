namespace InSalute.Web.Services;

public static class DateDisplay
{
    private static readonly TimeZoneInfo Rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");

    /// <summary>Data UTC mostrata in ora italiana, formato dd/MM/yyyy HH:mm.</summary>
    public static string ToLocalText(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Rome).ToString("dd/MM/yyyy HH:mm");
    }
}
