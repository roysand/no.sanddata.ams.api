using Domain.Common.Entities;

namespace Application.Common.Interfaces.Repositories;

public interface IRefreshTokenRepository : IRepository<RefreshToken>
{
    Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default);
}
