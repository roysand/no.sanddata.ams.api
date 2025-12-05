using Application.Common.Interfaces.Repositories;
using Domain.Common.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Database.Repositories;

public class RefreshTokenEfRepository : GenericEfRepository<RefreshToken>, IRefreshTokenRepository
{
    private readonly ApplicationDbContext _context;

    public RefreshTokenEfRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var tokens = await _context.Set<RefreshToken>()
            .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = DateTime.UtcNow;
            token.ReasonRevoked = "All tokens revoked";
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
