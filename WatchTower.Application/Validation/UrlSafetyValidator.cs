using System.Net;
using System.Net.Sockets;
using WatchTower.Application.Exceptions;
using WatchTower.Application.Options;

namespace WatchTower.Application.Validation;

public static class UrlSafetyValidator
{
    public static void ValidateTargetUrl(Uri uri, MonitoringOptions options)
    {
        if (!options.BlockPrivateAddresses)
            return;

        if (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            throw new ValidationException("URL target is not allowed.");

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IsPrivateOrReserved(ip))
            throw new ValidationException("URL target is not allowed.");
    }

    public static void ValidateTargetIp(IPAddress ip, MonitoringOptions options)
    {
        if (options.BlockPrivateAddresses && IsPrivateOrReserved(ip))
            throw new ValidationException("Private, loopback and link-local IP addresses can't be monitored.");
    }

    private static bool IsPrivateOrReserved(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 0
                || b[0] == 10
                || b[0] == 127
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168);
        }

        return ip.IsIPv6LinkLocal
            || ip.IsIPv6SiteLocal
            || ip.IsIPv6UniqueLocal;
    }
}
