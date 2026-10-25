namespace KultureCompass.Application.Abstractions;

/// <summary>The authenticated caller. <see cref="OwnerId"/> is the JWT <c>sub</c> (users.id as string). Only valid
/// inside an authenticated HTTP request — the background worker reads owner_id from the plan row instead.</summary>
public interface ICurrentUser
{
    string OwnerId { get; }
}
