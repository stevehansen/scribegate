using System.Security.Cryptography;
using System.Text;

namespace Scribegate.Core.ShareLinks;

public static class ShareLinkTokenDefaults
{
    public const string TokenPrefix = "sl_";
    public const int DefaultExpiryDays = 7;
    public const int MaxExpiryDays = 365;
    public const int TokenPrefixDisplayLength = 8;
}

public static class ShareLinkTokenService
{
    public static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return ShareLinkTokenDefaults.TokenPrefix + Convert.ToBase64String(bytes)
            .Replace("+", "")
            .Replace("/", "")
            .Replace("=", "");
    }

    public static string HashToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    public static string DisplayPrefix(string token) =>
        token.Length <= ShareLinkTokenDefaults.TokenPrefixDisplayLength
            ? token
            : token[..ShareLinkTokenDefaults.TokenPrefixDisplayLength];
}
