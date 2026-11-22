using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SupportTicketTriage.IntegrationTests;

/// The OpenAPI document is generated when it is requested, and generation can
/// throw on a type the schema generator cannot describe. A test that fetches
/// it is the only thing that proves the document exists at all — the build
/// says nothing about it.
[Collection(nameof(TicketApiCollection))]
public class OpenApiDocumentTests(TicketApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task TheDocumentIsServedAndCoversEveryTicketEndpoint()
    {
        var response = await _client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in (string[])
                 ["/tickets", "/tickets/{id}", "/tickets/{id}/similar", "/tickets/{id}/classification",
                  "/tickets/{id}/routing", "/tickets/{id}/draft", "/tickets/{id}/review", "/dashboard"])
        {
            Assert.True(paths.TryGetProperty(path, out _), $"{path} is missing from the document.");
        }
    }

    /// The reason the OpenAPI package earns its place: the handlers return
    /// Results&lt;Created&lt;T&gt;, NotFound, Conflict&lt;string&gt;, ValidationProblem&gt;,
    /// so these status codes come from the method signature. No
    /// [ProducesResponseType] attribute exists anywhere in this project to
    /// drift out of step with the code.
    [Fact]
    public async Task ResponseCodesAreInferredFromTheTypedResultsUnion()
    {
        using var document = JsonDocument.Parse(await _client.GetStringAsync("/openapi/v1.json"));

        var responses = document.RootElement
            .GetProperty("paths")
            .GetProperty("/tickets/{id}/review")
            .GetProperty("post")
            .GetProperty("responses");

        foreach (var code in (string[])["201", "400", "404", "409"])
        {
            Assert.True(responses.TryGetProperty(code, out _), $"{code} is missing from POST /tickets/{{id}}/review.");
        }
    }

    /// No-auto-send is an absence, and an absence is lost to a well-meaning
    /// addition rather than a deliberate one (AUDIT.md, finding 3). This
    /// closes the route set: every route the application maps is listed here,
    /// so adding one, a send endpoint included, fails until someone adds it
    /// to this list on purpose and a reviewer sees it in the diff.
    ///
    /// Read from the routing table rather than the OpenAPI document, which
    /// leaves out any route marked ExcludeFromDescription - the quiet way a
    /// route could be added. The framework's own routes (/health, the
    /// document, Scalar) are listed too, so the set really is closed. The
    /// Scalar entries are that package's detail and may change when it is
    /// upgraded; updating them then is the price of a closed list.
    [Fact]
    public void TheApplicationMapsExactlyTheseRoutes()
    {
        string[] expected =
        [
            "ANY /health",
            "GET /categories",
            "GET /dashboard",
            "GET /openapi/{documentName}.json",
            "GET /scalar/favicon.svg",
            "GET /scalar/scalar.aspnetcore.js",
            "GET /scalar/scalar.js",
            "GET /scalar/{documentName?}",
            "GET /tickets",
            "GET /tickets/{id:guid}",
            "GET /tickets/{id:guid}/classification",
            "GET /tickets/{id:guid}/draft",
            "GET /tickets/{id:guid}/review",
            "GET /tickets/{id:guid}/routing",
            "GET /tickets/{id:guid}/similar",
            "POST /tickets",
            "POST /tickets/{id:guid}/draft",
            "POST /tickets/{id:guid}/review",
            "POST /tickets/{id:guid}/triage",
        ];

        var mapped = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
                (endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"])
                .Select(method => $"{method} {endpoint.RoutePattern.RawText}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, mapped);
    }

    /// Pins the route Scalar is actually mounted on, which is a detail of the
    /// package rather than of our code and has moved between its major
    /// versions.
    [Fact]
    public async Task TheScalarReferenceIsServed()
    {
        var response = await _client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("text/html", response.Content.Headers.ContentType?.MediaType ?? string.Empty);
    }
}
