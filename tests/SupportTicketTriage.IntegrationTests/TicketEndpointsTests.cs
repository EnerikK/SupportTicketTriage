using System.Net;
using System.Net.Http.Json;
using SupportTicketTriage.Api.Contracts;

namespace SupportTicketTriage.IntegrationTests;

[Collection(nameof(TicketApiCollection))]
public class TicketEndpointsTests(TicketApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostThenGet_ReturnsTheCreatedTicket()
    {
        var request = new IngestTicketRequest("Can't log in", "I get an error on the login page.");

        var postResponse = await _client.PostAsJsonAsync("/tickets", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        var created = await postResponse.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.NotNull(created);
        Assert.Equal(request.Subject, created!.Subject);
        Assert.Equal(request.Body, created.Body);

        var getResponse = await _client.GetAsync($"/tickets/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var fetched = await getResponse.Content.ReadFromJsonAsync<TicketResponse>();
        Assert.Equal(created, fetched);

        var listResponse = await _client.GetFromJsonAsync<List<TicketResponse>>("/tickets");
        Assert.Contains(listResponse!, t => t.Id == created.Id);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/tickets/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("", "some body")]
    [InlineData("some subject", "")]
    public async Task Post_WithMissingField_ReturnsBadRequest(string subject, string body)
    {
        var response = await _client.PostAsJsonAsync("/tickets", new IngestTicketRequest(subject, body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
