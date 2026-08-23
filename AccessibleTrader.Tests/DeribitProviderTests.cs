using System.Linq;
using AccessibleTrader.Plugins.Deribit;
using Xunit;

namespace AccessibleTrader.Tests;

/// <summary>
/// Deribit crypto-options analytics: pin the two public-endpoint JSON shapes so a
/// silent parse regression (Deribit's classic {result:{data:[[...]]}} vs the flat
/// {result:[[...]]}) can't ship a provider that quietly returns nothing.
/// </summary>
[Collection("ProviderCredentialBridge")]
public class DeribitProviderTests
{
    [Fact]
    public void ParseDvol_reads_OHLC_rows_from_result_data()
    {
        // get_volatility_index_data: result.data = [[ts_ms, o, h, l, c], ...]
        const string json = """
        {"jsonrpc":"2.0","result":{"data":[
            [1700000000000, 50.1, 52.4, 49.8, 51.0],
            [1700003600000, 51.0, 51.9, 50.2, 50.6]
        ],"continuation":null},"usIn":1,"usOut":2}
        """;

        var bars = DeribitProvider.ParseDvol(json);

        Assert.Equal(2, bars.Count);
        Assert.Equal(50.1, bars[0].Open);
        Assert.Equal(52.4, bars[0].High);
        Assert.Equal(49.8, bars[0].Low);
        Assert.Equal(51.0, bars[0].Close);
        Assert.Equal(50.6, bars[1].Close);
    }

    [Fact]
    public void ParseHistVol_flattens_single_value_rows_to_OHLC()
    {
        // get_historical_volatility: result = [[ts_ms, value], ...]
        const string json = """
        {"jsonrpc":"2.0","result":[
            [1700000000000, 42.5],
            [1700003600000, 43.1]
        ],"usIn":1,"usOut":2}
        """;

        var bars = DeribitProvider.ParseHistVol(json);

        Assert.Equal(2, bars.Count);
        // Single value → flat candle (O=H=L=C) so it renders as a line.
        Assert.Equal(42.5, bars[0].Open);
        Assert.Equal(42.5, bars[0].High);
        Assert.Equal(42.5, bars[0].Low);
        Assert.Equal(42.5, bars[0].Close);
        Assert.Equal(43.1, bars[1].Close);
    }

    [Theory]
    [InlineData("{\"result\":{}}")]
    [InlineData("{\"result\":null}")]
    [InlineData("{}")]
    public void ParseDvol_missing_data_yields_empty_not_throw(string json)
    {
        Assert.Empty(DeribitProvider.ParseDvol(json));
    }

    [Fact]
    public void GetSymbolDisplayName_labels_both_metrics_readably()
    {
        var p = new DeribitProvider();
        Assert.Equal("BTC DVOL (Volatility Index)", p.GetSymbolDisplayName("BTC_DVOL"));
        Assert.Equal("ETH Realised Volatility", p.GetSymbolDisplayName("ETH_HISTVOL"));
    }

    [Fact]
    public async System.Threading.Tasks.Task Available_symbols_cover_DVOL_and_HISTVOL_for_BTC_and_ETH()
    {
        var syms = await new DeribitProvider().GetAvailableSymbolsAsync(Sdk.Enums.MarketType.Derivatives);
        Assert.Contains("BTC_DVOL", syms);
        Assert.Contains("ETH_DVOL", syms);
        Assert.Contains("BTC_HISTVOL", syms);
        Assert.Contains("ETH_HISTVOL", syms);
    }
}
