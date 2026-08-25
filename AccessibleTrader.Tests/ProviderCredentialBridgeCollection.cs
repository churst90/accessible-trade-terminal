namespace AccessibleTrader.Tests
{
    // Defines the "ProviderCredentialBridge" collection so xUnit actually SERIALIZES the
    // classes tagged [Collection("ProviderCredentialBridge")]. Without this definition the
    // tag is inert — the classes ran in parallel and raced the global PluginHostServices.ApiKeys
    // bridge (a no-credentials install in ProviderFetchOhlcvTests made Kraken's request-time
    // checkout return None, so BrokerParityTests.Kraken saw zero HTTP calls — the flake).
    [CollectionDefinition("ProviderCredentialBridge")]
    public sealed class ProviderCredentialBridgeCollection { }
}
