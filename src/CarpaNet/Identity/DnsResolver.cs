using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.Identity;

/// <summary>
/// Default DNS resolver implementation that queries DNS TXT records.
/// Uses raw UDP DNS queries for TXT record support.
/// </summary>
public sealed class DefaultDnsResolver : IDnsResolver
{
    private static readonly Random RandomInstance = new Random();
    private readonly string[] _dnsServers;
    private readonly int _timeout;

    /// <summary>
    /// Default DNS servers to use (Cloudflare and Google).
    /// </summary>
    public static readonly string[] DefaultDnsServers = new[] { "1.1.1.1", "8.8.8.8" };

    /// <summary>
    /// Creates a new DefaultDnsResolver with default settings.
    /// </summary>
    public DefaultDnsResolver() : this(DefaultDnsServers, 5000)
    {
    }

    /// <summary>
    /// Creates a new DefaultDnsResolver with custom settings.
    /// </summary>
    /// <param name="dnsServers">DNS server IP addresses to use.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    public DefaultDnsResolver(string[] dnsServers, int timeoutMs = 5000)
    {
        _dnsServers = dnsServers ?? DefaultDnsServers;
        _timeout = timeoutMs;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
    {
        var results = new List<string>();

        foreach (var server in _dnsServers)
        {
            try
            {
                var records = await QueryTxtRecordsAsync(name, server, cancellationToken).ConfigureAwait(false);
                if (records.Count > 0)
                {
                    return records;
                }
            }
            catch
            {
                // Try next server
            }
        }

        return results;
    }

    private async Task<List<string>> QueryTxtRecordsAsync(string name, string dnsServer, CancellationToken cancellationToken)
    {
        var results = new List<string>();

        // Build DNS query
        var query = BuildDnsQuery(name, DnsRecordType.TXT);

        using var client = new UdpClient();
        client.Client.ReceiveTimeout = _timeout;
        client.Client.SendTimeout = _timeout;

        var serverEndpoint = new IPEndPoint(IPAddress.Parse(dnsServer), 53);

        // Send query
        await client.SendAsync(query, query.Length, serverEndpoint).ConfigureAwait(false);

        // Receive response with timeout
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        var receiveTask = client.ReceiveAsync();
        var completedTask = await Task.WhenAny(receiveTask, Task.Delay(_timeout, cts.Token)).ConfigureAwait(false);

        if (completedTask != receiveTask)
        {
            throw new TimeoutException("DNS query timed out");
        }

        var response = await receiveTask.ConfigureAwait(false);

        // Parse response
        results = ParseTxtRecords(response.Buffer);

        return results;
    }

    private static byte[] BuildDnsQuery(string name, DnsRecordType recordType)
    {
        var query = new List<byte>();

        // Transaction ID (random)
        var transactionId = (ushort)RandomInstance.Next(0, 65536);
        query.Add((byte)(transactionId >> 8));
        query.Add((byte)(transactionId & 0xFF));

        // Flags: Standard query, recursion desired
        query.Add(0x01);
        query.Add(0x00);

        // Questions: 1
        query.Add(0x00);
        query.Add(0x01);

        // Answer RRs: 0
        query.Add(0x00);
        query.Add(0x00);

        // Authority RRs: 0
        query.Add(0x00);
        query.Add(0x00);

        // Additional RRs: 0
        query.Add(0x00);
        query.Add(0x00);

        // Question section
        foreach (var label in name.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }
        query.Add(0x00); // End of name

        // Query type (TXT = 16)
        query.Add(0x00);
        query.Add((byte)recordType);

        // Query class (IN = 1)
        query.Add(0x00);
        query.Add(0x01);

        return query.ToArray();
    }

    private static List<string> ParseTxtRecords(byte[] response)
    {
        var results = new List<string>();

        if (response.Length < 12)
            return results;

        // Parse header
        var flags = (response[2] << 8) | response[3];
        var rcode = flags & 0x0F;

        if (rcode != 0) // Non-zero RCODE means error
            return results;

        var questionCount = (response[4] << 8) | response[5];
        var answerCount = (response[6] << 8) | response[7];

        if (answerCount == 0)
            return results;

        // Skip header (12 bytes)
        var offset = 12;

        // Skip questions
        for (var i = 0; i < questionCount; i++)
        {
            offset = SkipName(response, offset);
            offset += 4; // Type (2) + Class (2)
        }

        // Parse answers
        for (var i = 0; i < answerCount; i++)
        {
            offset = SkipName(response, offset);

            if (offset + 10 > response.Length)
                break;

            var type = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            // Skip class
            offset += 2;

            // Skip TTL
            offset += 4;

            // Data length
            var dataLength = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            if (type == (int)DnsRecordType.TXT && offset + dataLength <= response.Length)
            {
                // TXT record: one or more length-prefixed strings
                var dataEnd = offset + dataLength;
                while (offset < dataEnd)
                {
                    var txtLength = response[offset++];
                    if (offset + txtLength > dataEnd)
                        break;

                    var txt = Encoding.UTF8.GetString(response, offset, txtLength);
                    results.Add(txt);
                    offset += txtLength;
                }
            }
            else
            {
                offset += dataLength;
            }
        }

        return results;
    }

    private static int SkipName(byte[] data, int offset)
    {
        while (offset < data.Length)
        {
            var length = data[offset];

            if (length == 0)
            {
                return offset + 1;
            }

            // Check for compression pointer (top 2 bits set)
            if ((length & 0xC0) == 0xC0)
            {
                return offset + 2;
            }

            offset += length + 1;
        }

        return offset;
    }

    private enum DnsRecordType : ushort
    {
        A = 1,
        AAAA = 28,
        TXT = 16,
        CNAME = 5,
        MX = 15
    }
}
