using Scribegate.Core.Entities;

namespace Scribegate.Core.Stores;

public interface IUserStore
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> FindByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> FindByOidcSubjectAsync(string issuer, string subject, CancellationToken ct = default);
    Task<bool> IsAdminAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> ListAdminIdsAsync(CancellationToken ct = default);
    Task<bool> AnyExistAsync(CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);

    Task<NotificationPreference?> GetNotificationPreferencesAsync(Guid userId, CancellationToken ct = default);
    Task UpsertNotificationPreferencesAsync(NotificationPreference prefs, CancellationToken ct = default);
}
