using System;
using System.Collections.Generic;
using System.Net.Http;

namespace CarpaNet;

/// <summary>
/// A single XRPC request, sent with <see cref="IXrpcRequestClient.SendXrpcAsync(XrpcRequest, System.Threading.CancellationToken)"/>.
/// </summary>
public sealed class XrpcRequest
{
    /// <summary>
    /// Creates a request.
    /// </summary>
    /// <param name="method"><see cref="HttpMethod.Get"/> for a query, <see cref="HttpMethod.Post"/> for a procedure.</param>
    /// <param name="nsid">The NSID of the method.</param>
    public XrpcRequest(HttpMethod method, string nsid)
    {
        if (string.IsNullOrEmpty(nsid))
        {
            throw new ArgumentException("NSID cannot be null or empty.", nameof(nsid));
        }

        Method = method ?? throw new ArgumentNullException(nameof(method));
        Nsid = nsid;
    }

    /// <summary>
    /// Gets the HTTP method.
    /// </summary>
    public HttpMethod Method { get; }

    /// <summary>
    /// Gets the NSID of the method.
    /// </summary>
    public string Nsid { get; }

    /// <summary>
    /// Gets or sets the query parameters.
    /// </summary>
    public IEnumerable<KeyValuePair<string, string>>? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the request body (procedures only).
    /// </summary>
    public XrpcBody? Body { get; set; }

    /// <summary>
    /// Gets or sets the per-request options.
    /// </summary>
    public XrpcRequestOptions? Options { get; set; }
}
