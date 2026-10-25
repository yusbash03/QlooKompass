using KultureCompass.Application.Abstractions;

namespace KultureCompass.Infrastructure.Identity;

/// <summary>Creates the optional judge/demo account from <c>Demo:UserEmail</c>/<c>Demo:UserPassword</c>. The Api only
/// calls this outside Production. Idempotent: an existing account is left untouched.</summary>
public sealed class DemoUserSeeder(IUserAccounts accounts)
{
    public async Task<bool> SeedAsync(string email, string password, CancellationToken ct)
    {
        var result = await accounts.CreateAsync(email, password, "Demo user", ct).ConfigureAwait(false);
        return result.Status switch
        {
            CreateAccountStatus.Created => true,
            CreateAccountStatus.EmailTaken => false,
            _ => throw new InvalidOperationException(
                "Demo:UserPassword does not satisfy the password policy: " + string.Join(" ", result.PasswordErrors)),
        };
    }
}
