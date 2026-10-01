using ECommerce.BuildingBlocks.Domain;

namespace ECommerce.Modules.Identity.Domain;

public class User : AggregateRoot<Guid>
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsEmailVerified { get; private set; } = false;
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private readonly List<UserRole> _roles = new();
    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    private readonly List<RefreshToken> _refreshTokens = new();
    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();

    private User() { } // EF Core

    public static User Create(string email, string passwordHash, string fullName, string? phoneNumber = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            PhoneNumber = phoneNumber,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        return user;
    }

    public void AddRole(Guid roleId)
    {
        if (!_roles.Any(r => r.RoleId == roleId))
        {
            _roles.Add(new UserRole { UserId = Id, RoleId = roleId, AssignedAt = DateTime.UtcNow });
        }
    }

    public RefreshToken AddRefreshToken(string tokenHash, DateTime expiresAt)
    {
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Id,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };
        _refreshTokens.Add(token);
        return token;
    }

    public void RevokeRefreshToken(string tokenHash, string? replacedBy = null)
    {
        var token = _refreshTokens.FirstOrDefault(x => x.TokenHash == tokenHash);
        if (token != null)
        {
            token.Revoke(replacedBy);
        }
    }
}

public class Role
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; private set; } = false;
    public string? ReplacedByTokenHash { get; private set; }
    public DateTime CreatedAt { get; set; }

    public bool IsActive => !IsRevoked && DateTime.UtcNow < ExpiresAt;

    public void Revoke(string? replacedBy = null)
    {
        IsRevoked = true;
        ReplacedByTokenHash = replacedBy;
    }
}
