using Microsoft.AspNetCore.Identity;

namespace KultureCompass.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
