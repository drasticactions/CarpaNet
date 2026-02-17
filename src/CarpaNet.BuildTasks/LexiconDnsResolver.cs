using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CarpaNet.BuildTasks;

/// <summary>
/// DNS TXT record resolver for lexicon authority lookup.
/// Adapted from CarpaNet.Identity.DefaultDnsResolver for use in MSBuild tasks.
/// </summary>
internal sealed class LexiconDnsResolver
{
    private static readonly Random RandomInstance = new();
    private readonly string[] _dnsServers;
    private readonly int _timeout;

    public static readonly string[] DefaultDnsServers = ["1.1.1.1", "8.8.8.8"];

    public LexiconDnsResolver(string[]? dnsServers = null, int timeoutMs = 5000)
    {
        _dnsServers = dnsServers ?? DefaultDnsServers;
        _timeout = timeoutMs;
    }

    public async Task<IReadOnlyList<string>> GetTxtRecordsAsync(string name, CancellationToken cancellationToken = default)
    {
        foreach (var server in _dnsServers)
        {
            try
            {
                var records = await QueryTxtRecordsAsync(name, server, cancellationToken).ConfigureAwait(false);
                if (records.Count > 0)
                    return records;
            }
            catch
            {
                // Try next server
            }
        }

        return Array.Empty<string>();
    }

    private async Task<List<string>> QueryTxtRecordsAsync(string name, string dnsServer, CancellationToken cancellationToken)
    {
        var query = BuildDnsQuery(name);

        using var client = new UdpClient();
        client.Client.ReceiveTimeout = _timeout;
        client.Client.SendTimeout = _timeout;

        var serverEndpoint = new IPEndPoint(IPAddress.Parse(dnsServer), 53);

        await client.SendAsync(query, query.Length, serverEndpoint).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        var receiveTask = client.ReceiveAsync();
        var completedTask = await Task.WhenAny(receiveTask, Task.Delay(_timeout, cts.Token)).ConfigureAwait(false);

        if (completedTask != receiveTask)
            throw new TimeoutException("DNS query timed out");

        var response = await receiveTask.ConfigureAwait(false);
        return ParseTxtRecords(response.Buffer);
    }

    private static byte[] BuildDnsQuery(string name)
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

        // Answer/Authority/Additional RRs: 0
        query.Add(0x00); query.Add(0x00);
        query.Add(0x00); query.Add(0x00);
        query.Add(0x00); query.Add(0x00);

        // Question section
        foreach (var label in name.Split('.'))
        {
            query.Add((byte)label.Length);
            query.AddRange(Encoding.ASCII.GetBytes(label));
        }
        query.Add(0x00); // End of name

        // Query type: TXT = 16
        query.Add(0x00);
        query.Add(0x10);

        // Query class: IN = 1
        query.Add(0x00);
        query.Add(0x01);

        return query.ToArray();
    }

    private static List<string> ParseTxtRecords(byte[] response)
    {
        var results = new List<string>();

        if (response.Length < 12)
            return results;

        var flags = (response[2] << 8) | response[3];
        var rcode = flags & 0x0F;

        if (rcode != 0)
            return results;

        var questionCount = (response[4] << 8) | response[5];
        var answerCount = (response[6] << 8) | response[7];

        if (answerCount == 0)
            return results;

        var offset = 12;

        // Skip questions
        for (var i = 0; i < questionCount; i++)
        {
            offset = SkipName(response, offset);
            offset += 4; // Type + Class
        }

        // Parse answers
        for (var i = 0; i < answerCount; i++)
        {
            offset = SkipName(response, offset);

            if (offset + 10 > response.Length)
                break;

            var type = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            // Skip class + TTL
            offset += 6;

            // Data length
            var dataLength = (response[offset] << 8) | response[offset + 1];
            offset += 2;

            if (type == 16 /* TXT */ && offset + dataLength <= response.Length)
            {
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
                return offset + 1;

            // Compression pointer
            if ((length & 0xC0) == 0xC0)
                return offset + 2;

            offset += length + 1;
        }

        return offset;
    }
}
