using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Scribegate.Core.Entities;

namespace Scribegate.Web.Api;

public class JwtService(IConfiguration configuration)
{
    private const int DefaultExpirationHours = 24;

    public string GenerateToken(User user)
    {
        var key = GetSigningKey();
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("username", user.Username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var expiration = DateTime.UtcNow.AddHours(
            configuration.GetValue("Scribegate:Jwt:ExpirationHours", DefaultExpirationHours));

        var token = new JwtSecurityToken(
            issuer: configuration["Scribegate:Jwt:Issuer"] ?? "scribegate",
            audience: configuration["Scribegate:Jwt:Audience"] ?? "scribegate",
            claims: claims,
            expires: expiration,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime GetExpiration() =>
        DateTime.UtcNow.AddHours(
            configuration.GetValue("Scribegate:Jwt:ExpirationHours", DefaultExpirationHours));

    public SymmetricSecurityKey GetSigningKey()
    {
        var secret = configuration["Scribegate:Jwt:Secret"];
        if (string.IsNullOrEmpty(secret) || secret.Length < 32)
        {
            // Auto-generate a key for development. In production, this should be set explicitly.
            // We use a deterministic key based on the data path so it survives restarts.
            var dataPath = configuration["Scribegate:DataPath"] ?? "data";
            var keyFile = Path.Combine(dataPath, ".jwt-key");

            if (File.Exists(keyFile))
            {
                secret = File.ReadAllText(keyFile);
            }
            else
            {
                secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
                Directory.CreateDirectory(dataPath);
                File.WriteAllText(keyFile, secret);
            }
        }

        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }
}
