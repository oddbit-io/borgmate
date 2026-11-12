using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace BorgMate.Infrastructure.Platform;

/// <summary>
///     Validates an SSH destination (<c>host</c> or <c>user@host</c>). Borg passes the destination to
///     ssh without <c>--</c>, so a leading <c>-</c> in either part would turn it into an ssh option
///     such as <c>-oProxyCommand=…</c>.
/// </summary>
public static class SshDestination
{
    public static bool IsValid(string destination)
    {
        var atIndex = destination.LastIndexOf('@');
        return atIndex < 0
            ? IsValidHost(destination)
            : IsValidName(destination[..atIndex]) && IsValidHost(destination[(atIndex + 1)..]);
    }

    public static bool IsValid(string user, string host)
    {
        return IsValid(string.IsNullOrEmpty(user) ? host : $"{user}@{host}");
    }

    /// <summary>Returns the index of the colon that ends the host in <c>[user@]host:path</c>, or -1.</summary>
    public static int HostEndIndex(string location)
    {
        var hostStart = location.IndexOf('@') + 1;
        var searchFrom = hostStart;
        if (location.Length > hostStart && location[hostStart] == '[')
        {
            var bracketEnd = location.IndexOf(']', hostStart);
            if (bracketEnd > 0)
                searchFrom = bracketEnd;
        }

        return location.IndexOf(':', searchFrom);
    }

    private static bool IsValidHost(string host)
    {
        return host.StartsWith('[') && host.EndsWith(']')
            ? IPAddress.TryParse(host[1..^1], out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            : IsValidName(host);
    }

    private static bool IsValidName(string part)
    {
        return part.Length > 0 && !part.StartsWith('-') &&
               !part.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '@' or '/' or ':');
    }
}
