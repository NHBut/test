using ECommerce.BuildingBlocks.CQRS;
using ECommerce.Modules.Identity.Domain;
using ECommerce.Modules.Identity.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ECommerce.Modules.Identity.Application;

public record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt, Guid UserId, string Email, string FullName);

public record RegisterCommand(string Email, string Password, string FullName, string? PhoneNumber) : ICommand<AuthResponse>;

public record LoginCommand(string Email, string Password) : ICommand<AuthResponse>;

public record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;

public class AuthCommandHandler : 
    ICommandHandler<RegisterCommand, AuthResponse>,
    ICommandHandler<LoginCommand, AuthResponse>,
    ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    private readonly IdentityDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokenService;

    public AuthCommandHandler(IdentityDbContext db, IPasswordHasher hasher, ITokenService tokenService)
    {
        _db = db;
        _hasher = hasher;
        _tokenService = tokenService;
    }

    public async Task<AuthResponse> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(x => x.Email == email, cancellationToken))
        {
            throw new InvalidOperationException($"User with email '{email}' already exists.");
        }

        var hash = _hasher.HashPassword(request.Password);
        var user = User.Create(email, hash, request.FullName, request.PhoneNumber);

        // Mặc định gán role 'Customer'
        var customerRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Customer", cancellationToken);
        if (customerRole == null)
        {
            customerRole = new Role { Name = "Customer", Description = "Default customer role" };
            _db.Roles.Add(customerRole);
            await _db.SaveChangesAsync(cancellationToken);
        }
        user.AddRole(customerRole.Id);

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);
        user.AddRefreshToken(hashedRefreshToken, DateTime.UtcNow.AddDays(7));

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, new[] { "Customer" });
        return new AuthResponse(accessToken, rawRefreshToken, DateTime.UtcNow.AddMinutes(15), user.Id, user.Email, user.FullName);
    }

    public async Task<AuthResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (user == null || !_hasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        var rawRefreshToken = _tokenService.GenerateRefreshToken();
        var hashedRefreshToken = _tokenService.HashToken(rawRefreshToken);
        user.AddRefreshToken(hashedRefreshToken, DateTime.UtcNow.AddDays(7));
        await _db.SaveChangesAsync(cancellationToken);

        var roleNames = await _db.Roles
            .Where(r => user.Roles.Select(ur => ur.RoleId).Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, roleNames);
        return new AuthResponse(accessToken, rawRefreshToken, DateTime.UtcNow.AddMinutes(15), user.Id, user.Email, user.FullName);
    }

    public async Task<AuthResponse> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var hashedToken = _tokenService.HashToken(request.RefreshToken);
        var tokenRecord = await _db.RefreshTokens
            .Include(t => t.UserId)
            .FirstOrDefaultAsync(t => t.TokenHash == hashedToken, cancellationToken);

        if (tokenRecord == null || !tokenRecord.IsActive)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        var user = await _db.Users.Include(x => x.Roles).FirstAsync(x => x.Id == tokenRecord.UserId, cancellationToken);

        // Rotation: Revoke token cũ, cấp token mới
        var newRawRefreshToken = _tokenService.GenerateRefreshToken();
        var newHashedRefreshToken = _tokenService.HashToken(newRawRefreshToken);
        user.RevokeRefreshToken(hashedToken, newHashedRefreshToken);
        user.AddRefreshToken(newHashedRefreshToken, DateTime.UtcNow.AddDays(7));
        await _db.SaveChangesAsync(cancellationToken);

        var roleNames = await _db.Roles
            .Where(r => user.Roles.Select(ur => ur.RoleId).Contains(r.Id))
            .Select(r => r.Name)
            .ToListAsync(cancellationToken);

        var accessToken = _tokenService.GenerateAccessToken(user, roleNames);
        return new AuthResponse(accessToken, newRawRefreshToken, DateTime.UtcNow.AddMinutes(15), user.Id, user.Email, user.FullName);
    }
}
