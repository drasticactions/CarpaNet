using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CarpaNet.Http;
using Xunit;

namespace CarpaNet.UnitTests.Http;

/// <summary>
/// Procedures without an output are generated as <c>Task&lt;object&gt;</c>; their responses must
/// not be deserialized.
/// </summary>
public class NoOutputProcedureTests
{
    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"unexpected\":true}")]
    public async Task ProcessResponseAsync_ObjectOutput_IgnoresBody(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, body.Length == 0 ? "text/plain" : "application/json"),
        };

        var result = await XrpcHttpHandler.ProcessResponseAsync<object>(response, TestHelpers.CreateJsonOptions());

        Assert.NotNull(result);
    }

    [Fact]
    public async Task ProcessResponseAsync_ObjectOutput_StillThrowsForErrors()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"InvalidRequest\",\"message\":\"bad\"}", Encoding.UTF8, "application/json"),
        };

        var ex = await Assert.ThrowsAnyAsync<ATProtoException>(() =>
            XrpcHttpHandler.ProcessResponseAsync<object>(response, TestHelpers.CreateJsonOptions()));
        Assert.Equal("InvalidRequest", ex.ErrorCode);
    }
}
