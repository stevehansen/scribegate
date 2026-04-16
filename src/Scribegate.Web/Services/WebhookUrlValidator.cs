using System.Net;
using System.Net.Sockets;

namespace Scribegate.Web.Services;

public static class WebhookUrlValidator
{
    private static readonly string[] BlockedHostnames =
    [
        "localhost",
        "metadata.google.internal",
        "metadata",
    ];

    public static bool IsAllowedUrl(Uri uri, bool allowPrivate)
    {
        if (allowPrivate) return true;

        foreach (var blocked in BlockedHostnames)
            if (string.Equals(uri.Host, blocked, StringComparison.OrdinalIgnoreCase))
                return false;

        if (IPAddress.TryParse(uri.Host, out var ip))
            return !IsPrivateOrLocal(ip);

        return true;
    }

    public static bool IsPrivateOrLocal(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 0) return true;
            if (b[0] == 10) return true;
            if (b[0] == 127) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] >= 224) return true;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast) return true;
            if (ip.Equals(IPAddress.IPv6Loopback)) return true;
            if (ip.IsIPv4MappedToIPv6) return IsPrivateOrLocal(ip.MapToIPv4());

            var b = ip.GetAddressBytes();
            if ((b[0] & 0xfe) == 0xfc) return true;
        }

        return false;
    }
}
