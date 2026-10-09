using Domain.Common;

namespace Application.Common.Interfaces.Repositories;

/// <summary>One user-location link as the users list shows it.</summary>
public record UserLinkInfo(Guid UserId, Guid LocationId, string LocationName, LocationRole Role);

/// <summary>One user-location link as the admin locations list shows it.</summary>
public record LocationUserInfo(
    Guid LocationId, Guid UserId, string Email, string FirstName, string LastName, LocationRole Role);

public interface IUserLocationRepository<T> : IRepository<T> where T : class
{
    /// <summary>True if the user has an owner link to the location. Does not care whether the location is active.</summary>
    Task<bool> IsOwnerAsync(Guid userId, Guid locationId, CancellationToken cancellationToken);

    Task<int> CountOwnersAsync(Guid locationId, CancellationToken cancellationToken);

    /// <summary>Every link of the given users, with the location's name.</summary>
    Task<IReadOnlyList<UserLinkInfo>> GetForUsersAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Every link of the given locations, with the user's details.</summary>
    Task<IReadOnlyList<LocationUserInfo>> GetForLocationsAsync(
        IReadOnlyCollection<Guid> locationIds, CancellationToken cancellationToken);
}
