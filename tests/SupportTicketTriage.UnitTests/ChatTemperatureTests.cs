using Microsoft.Extensions.AI;
using SupportTicketTriage.Infrastructure.Ai;

namespace SupportTicketTriage.UnitTests;

/// Reasoning models reject a temperature, so it is per-deployment
/// configuration. These pin the three states and the refusal to guess.
public class ChatTemperatureTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_KeepsTheExistingBehaviour_OfZero(string? configured) =>
        Assert.Equal(0f, AzureOpenAiOptions.ParseTemperature(configured));

    [Theory]
    [InlineData("omit")]
    [InlineData("OMIT")]
    [InlineData(" omit ")]
    public void Omit_SendsNoTemperature(string configured) =>
        Assert.Null(AzureOpenAiOptions.ParseTemperature(configured));

    /// Invariant culture: a comma-decimal locale must not read "0.7" as 7.
    [Fact]
    public void ANumber_IsReadInTheInvariantCulture() =>
        Assert.Equal(0.7f, AzureOpenAiOptions.ParseTemperature("0.7"));

    /// Falling back to 0 would send a temperature to a reasoning model that
    /// rejects it, and every call would fail quietly as "unclassified".
    [Theory]
    [InlineData("warm")]
    [InlineData("-1")]
    [InlineData("2.5")]
    [InlineData("0,7")]
    public void AnythingElse_FailsAtStartup_RatherThanFallingBack(string configured) =>
        Assert.Throws<InvalidOperationException>(() => AzureOpenAiOptions.ParseTemperature(configured));

    [Fact]
    public void Omitted_LeavesTheRequestPropertyUnset()
    {
        var options = JsonChatOptions.For(null);

        Assert.Null(options.Temperature);
        Assert.Equal(ChatResponseFormat.Json, options.ResponseFormat);
    }

    [Fact]
    public void Configured_IsSent() =>
        Assert.Equal(0f, JsonChatOptions.For(0f).Temperature);
}
