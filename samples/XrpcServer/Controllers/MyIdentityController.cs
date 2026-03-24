using CarpaNet.AspNetCore;
using Microsoft.AspNetCore.Http.HttpResults;

namespace XrpcServer.Controllers;

/// <summary>
/// Sample implementation of the generated abstract IdentityController.
/// </summary>
public class MyIdentityController : Xrpc.ComAtproto.Identity.IdentityController
{
    /// <inheritdoc/>
    public override Task<Results<Ok<ComAtproto.Identity.ResolveHandleOutput>, ATErrorResult>> ResolveHandleAsync(
        string handle,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(handle))
        {
            return Task.FromResult<Results<Ok<ComAtproto.Identity.ResolveHandleOutput>, ATErrorResult>>(
                ATErrorResult.BadRequest("Handle is required"));
        }

        var output = new ComAtproto.Identity.ResolveHandleOutput
        {
            Did = new CarpaNet.ATDid("did:plc:example123"),
        };

        return Task.FromResult<Results<Ok<ComAtproto.Identity.ResolveHandleOutput>, ATErrorResult>>(
            TypedResults.Ok(output));
    }
}
