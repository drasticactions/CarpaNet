using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CarpaNet.Blob;
using CarpaNet.Identity;
using CarpaNet.UnitTests.Http;
using Xunit;

namespace CarpaNet.UnitTests.Blob;

/// <summary>
/// Tests for blob upload and download through the client's own request pipeline.
/// </summary>
public class BlobPipelineTests
{
    private const string Cid = "bafkreibme22gw2h7y2h7tg2fhqotaqjucnbc24deqo72b6mkl2egezxhvy";

    [Fact]
    public async Task UploadBlobAsync_StreamsThroughClientAuth_AndReturnsBlobRef()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(
            $"{{\"blob\":{{\"$type\":\"blob\",\"ref\":{{\"$link\":\"{Cid}\"}},\"mimeType\":\"image/png\",\"size\":4}}}}"));
        using var client = XrpcRequestPipelineTests.CreateSessionClient(handler, out var accessJwt);
        var data = new byte[] { 1, 2, 3, 4 };

        var blob = await client.UploadBlobAsync(new MemoryStream(data), "image/png");

        var request = Assert.Single(handler.Requests);
        Assert.Equal("/xrpc/com.atproto.repo.uploadBlob", request.Uri.AbsolutePath);
        Assert.Equal("Bearer " + accessJwt, request.Header("Authorization"));
        Assert.Equal("image/png", request.ContentType);
        Assert.Equal(data, request.Body);
        Assert.Equal(Cid, blob.Ref!.Link);

        var atBlob = blob.ToATBlob();
        Assert.Equal(Cid, atBlob.Ref.ToString());
        Assert.Equal("image/png", atBlob.MimeType);
        Assert.Equal(4, atBlob.Size);
    }

    [Fact]
    public async Task UploadBlobAsync_DoesNotDisposeCallerStream()
    {
        var handler = new RecordingHandler(_ => XrpcRequestPipelineTests.Json(
            $"{{\"blob\":{{\"$type\":\"blob\",\"ref\":{{\"$link\":\"{Cid}\"}},\"mimeType\":\"image/png\",\"size\":1}}}}"));
        using var client = XrpcRequestPipelineTests.CreateSessionClient(handler, out _);
        using var stream = new MemoryStream(new byte[] { 9 });

        await client.UploadBlobAsync(stream, "image/png");

        Assert.True(stream.CanRead);
    }

    [Fact]
    public void ToATBlob_WithoutCid_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => new BlobRef().ToATBlob());
    }

    [Fact]
    public async Task DownloadBlobAsync_OtherAccount_UsesOwnerPdsWithoutCredentials()
    {
        var bytes = Encoding.UTF8.GetBytes("image");
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var cache = new MemoryIdentityCache();
        await cache.SetDidDocumentAsync("did:plc:other", new DidDocument
        {
            Id = "did:plc:other",
            Service = new List<DidService>
            {
                new DidService { Id = "#atproto_pds", Type = "AtprotoPersonalDataServer", ServiceEndpoint = "https://pds.other.example" },
            },
        });
        using var client = XrpcRequestPipelineTests.CreateSessionClient(handler, out _, new IdentityResolver(new HttpClient(handler), cache: cache));

        var result = await client.DownloadBlobAsync(new ATDid("did:plc:other"), new ATCid(Cid));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("pds.other.example", request.Uri.Host);
        Assert.Null(request.Header("Authorization"));
        Assert.Contains("did=did%3Aplc%3Aother", request.Uri.Query);
        Assert.Equal(bytes, result);
    }

    [Fact]
    public async Task DownloadBlobAsync_OwnAccount_UsesOwnPdsWithCredentials()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 1 }) });
        using var client = XrpcRequestPipelineTests.CreateSessionClient(handler, out var accessJwt);

        await client.DownloadBlobAsync(new ATDid("did:plc:user"), new ATCid(Cid));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("pds.user.example", request.Uri.Host);
        Assert.Equal("Bearer " + accessJwt, request.Header("Authorization"));
    }
}
