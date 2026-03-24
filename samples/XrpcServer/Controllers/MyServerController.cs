using CarpaNet.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;

namespace XrpcServer.Controllers;

/// <summary>
/// Sample implementation of the generated abstract ServerController.
/// Demonstrates how to implement XRPC server endpoints using CarpaNet-generated controllers.
/// </summary>
public class MyServerController : Xrpc.ComAtproto.Server.ServerController
{
    /// <inheritdoc/>
    public override Task<Results<Ok<ComAtproto.Server.DescribeServerOutput>, ATErrorResult>> DescribeServerAsync(
        CancellationToken cancellationToken = default)
    {
        var output = new ComAtproto.Server.DescribeServerOutput
        {
            AvailableUserDomains = new List<string> { ".example.com" },
            Did = new CarpaNet.ATDid("did:web:example.com"),
        };

        return Task.FromResult<Results<Ok<ComAtproto.Server.DescribeServerOutput>, ATErrorResult>>(
            TypedResults.Ok(output));
    }
}
