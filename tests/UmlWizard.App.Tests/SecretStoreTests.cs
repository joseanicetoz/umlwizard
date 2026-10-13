using UmlWizard.App.Services;

namespace UmlWizard.App.Tests;

public sealed class SecretStoreTests
{
    [Fact]
    public void MacKeychain_SetsTheSecretThroughInput_NotOnTheCommandLine()
    {
        var (file, arguments, input) = SystemSecretStore.MacKeychain.Set("ai.Anthropic.apiKey", "sk-ant-secret");

        Assert.Equal("security", file);
        Assert.DoesNotContain(arguments, a => a.Contains("sk-ant-secret", StringComparison.Ordinal));
        Assert.Equal("add-generic-password -U -s \"UmlWizard\" -a \"ai.Anthropic.apiKey\" -w \"sk-ant-secret\"\n", input);
    }

    [Theory]
    [InlineData("plain", "\"plain\"")]
    [InlineData("with space", "\"with space\"")]
    [InlineData("a\"b", "\"a\\\"b\"")]
    [InlineData(@"a\b", "\"a\\\\b\"")]
    public void Quote_EscapesQuotesAndBackslashes(string word, string expected) =>
        Assert.Equal(expected, SystemSecretStore.Quote(word));
}
