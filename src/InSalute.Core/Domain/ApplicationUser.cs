using Microsoft.AspNetCore.Identity;

namespace InSalute.Core.Domain;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";

    /// <summary>Gli utenti non si cancellano: si disattivano.</summary>
    public bool IsActive { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Id nel vecchio database, valorizzato solo dall'import.</summary>
    public long? LegacyId { get; set; }
}
