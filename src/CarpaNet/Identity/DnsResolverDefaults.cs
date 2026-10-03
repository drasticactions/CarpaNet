using System;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace CarpaNet.Identity;

/// <summary>
/// Selects the default <see cref="IDnsResolver"/> for the current platform.
/// </summary>
public static class DnsResolverDefaults
{
    /// <summary>
    /// Gets a value indicating whether the current platform can send raw UDP DNS queries.
    /// This is false in browsers (WebAssembly) and under WASI.
    /// </summary>
    public static bool IsUdpDnsSupported { get; } = !IsSandboxedPlatform();

    /// <summary>
    /// Creates the default DNS resolver for the current platform.
    /// </summary>
    /// <remarks>
    /// Returns a <see cref="DnsOverHttpsResolver"/> (with <see cref="DnsOverHttpsResolver.DefaultEndpoints"/>)
    /// when <see cref="IsUdpDnsSupported"/> is false, and a <see cref="DefaultDnsResolver"/> (UDP) otherwise.
    /// On networks that block UDP port 53, create a <see cref="DnsOverHttpsResolver"/> directly.
    /// </remarks>
    /// <param name="httpClient">The HttpClient for DNS-over-HTTPS requests. It is not used by the UDP resolver, and it is not disposed.</param>
    /// <returns>The DNS resolver.</returns>
    public static IDnsResolver CreateDefault(HttpClient httpClient)
    {
        if (httpClient == null)
            throw new ArgumentNullException(nameof(httpClient));

        return IsUdpDnsSupported
            ? new DefaultDnsResolver()
            : new DnsOverHttpsResolver(httpClient);
    }

    private static bool IsSandboxedPlatform()
    {
#if NET8_0_OR_GREATER
        return OperatingSystem.IsBrowser() || OperatingSystem.IsWasi();
#else
        return RuntimeInformation.IsOSPlatform(OSPlatform.Create("BROWSER"))
            || RuntimeInformation.IsOSPlatform(OSPlatform.Create("WASI"));
#endif
    }
}
