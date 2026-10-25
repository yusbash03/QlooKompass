using KultureCompass.Application.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace KultureCompass.Infrastructure.Identity;

/// <summary>Identity owns hashing, lockout and security stamps. Login mirrors
/// <c>SignInManager.CheckPasswordSignInAsync(lockoutOnFailure: true)</c> step-for-step using UserManager directly,
/// which keeps ASP.NET HTTP types (SignInManager lives in the web shared framework) out of Infrastructure.</summary>
public sealed class UserAccounts(UserManager<AppUser> users, TimeProvider clock) : IUserAccounts
{
    private static readonly AppUser TimingDummy = new() { Id = Guid.Empty, UserName = "timing-dummy" };

    public async Task<CreateAccountResult> CreateAsync(string email, string password, string? displayName, CancellationToken ct)
    {
        if (await users.FindByEmailAsync(email) is not null)
            return new(CreateAccountStatus.EmailTaken, null, []);

        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
            CreatedAt = clock.GetUtcNow(),
        };

        var result = await users.CreateAsync(user, password);
        if (result.Succeeded)
            return new(CreateAccountStatus.Created, ToAccount(user), []);

        // DuplicateEmail/DuplicateUserName = lost a race with a concurrent register for the same email.
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new(CreateAccountStatus.EmailTaken, null, []);

        var passwordErrors = result.Errors.Where(e => e.Code.StartsWith("Password", StringComparison.Ordinal)).ToList();
        if (passwordErrors.Count > 0)
            return new(CreateAccountStatus.PasswordRejected, null, [.. passwordErrors.Select(e => e.Description)]);

        throw new InvalidOperationException(
            $"User creation failed: {string.Join(", ", result.Errors.Select(e => e.Code))}");
    }

    public async Task<PasswordCheckResult> CheckPasswordAsync(string email, string password, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            // Spend roughly the same time as a real check so response timing doesn't reveal which emails exist.
            users.PasswordHasher.HashPassword(TimingDummy, password);
            return new(PasswordCheckStatus.InvalidCredentials, null);
        }

        if (await users.IsLockedOutAsync(user))
            return new(PasswordCheckStatus.LockedOut, null);

        if (await users.CheckPasswordAsync(user, password))
        {
            await users.ResetAccessFailedCountAsync(user);
            return new(PasswordCheckStatus.Succeeded, ToAccount(user));
        }

        await users.AccessFailedAsync(user);
        return await users.IsLockedOutAsync(user)
            ? new(PasswordCheckStatus.LockedOut, null)
            : new(PasswordCheckStatus.InvalidCredentials, null);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : ToAccount(user);
    }

    private static UserAccount ToAccount(AppUser user) =>
        new(user.Id, user.Email!, user.DisplayName, user.CreatedAt, user.SecurityStamp ?? string.Empty);
}
