using AstraClient.Models;

namespace AstraClient.Services.Auth;

public interface IAuthProvider
{
    Task<UserProfile?> AuthenticateAsync(CancellationToken ct = default);
    bool IsAvailable { get; }
}
