using System.Buffers.Binary;
using System.Security.Cryptography;
using AccessibleTrader.Core.Services;
using AccessibleTrader.Core.Services.Indicators;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Tests;

/// <summary>
/// Pins every Cipher B output array before its WaveTrend and stochastic arithmetic moves to
/// <see cref="WaveTrendMath"/>. Each line is a SHA-256 of the array's IEEE-754 double bits, including
/// NaNs, so a change to any output bar is visible. The gold dot's line went with the dot on
/// 2026-10-22; every other line is unchanged (the removal moved no other array, checked against the
/// commit before it on four probe series, two lengths and six parameter sets). The RSI and Stochastic
/// lines were re-pinned on 2026-10-23 when they moved to MCB 2.4.0's measured formula (close, 40/81
/// bars, SMA 2); every other line, the buy dot's included on this series, stayed the same. The money
/// flow line was re-pinned later that day when each Heikin-Ashi body moved from its own range to the
/// five-bar average range (scale 146); no other line moved. Re-pinned again the same day when the
/// divisor became the five-bar average body size (scale 68); again only the money flow moved. And on
/// 2026-10-24 for Market Cipher B's exact money flow (typical price less its five-bar mean, over the
/// five-bar mean size of that, scale 200/3); again only the money flow moved. On 2026-10-31 the parts
/// Market Cipher B does not draw were removed (the anchor waves and polarity, the red Overbought
/// Crossover, the four divergences with their pivot-line arrays, the adaptive-threshold arrays and
/// the AnchorMultiplier setting): their lines were deleted from these pins and every line left kept
/// its hash, and AnchorMultiplier = 2, now no setting, computes the defaults.
/// </summary>
public sealed class CipherBWaveTrendParityTests
{
    private const string DefaultsFingerprint = """
Money Flow Wave 5C2937388E774984D4A41F887441782637F8376F89B095805ADC2EDC4E99F3D8
Oversold Crossover 62252453974A12C6017B36AF6703AC0D9AB97076DAC68E90219D11CBA88D11A9
RSI 03BBF0ADD69C6F74FF7D246FC3C7D162DA3A55CF7100BD6A5B27946068A9C9EC
Stochastic 9A6DE42668BA1181C67EF7ED68E1D5B62B03E5ECA19FF20467E5B3D0FBE8180B
WT Histogram 5478F7C486BFA73F1479F4EB811E18CFF70DF6B811075F88DCA6563265366CC4
Wave Trend 519CD85E36890F5427642B66A5722D15E1DBB0397E0B79C445CACA30A00C0210
Wave Trend 2 04DBBE4F51A7557AE8F189AD8454EC4354DF9896ACA32F8AD0D174A4E839AC2B
WaveTrend Cross Bear 019A7CB729BBBA90436B4776F221D9A56439426262430CBF3075B291623B43C4
WaveTrend Cross Bull D6FA12A828AC0088EEB05A29B4EA750716287CB57D8A8369BE11DADA3A71E0BB
""";

    private const string SignalFourFingerprint = """
Money Flow Wave 5C2937388E774984D4A41F887441782637F8376F89B095805ADC2EDC4E99F3D8
Oversold Crossover D9B44D3110355D90097749387BBED63425BB034F59858A9BAE35119B50553E41
RSI 03BBF0ADD69C6F74FF7D246FC3C7D162DA3A55CF7100BD6A5B27946068A9C9EC
Stochastic 9A6DE42668BA1181C67EF7ED68E1D5B62B03E5ECA19FF20467E5B3D0FBE8180B
WT Histogram 03A6675B03C78661F2E6A513BF91864D5582D78C1037540B7DDA2E1C3C891484
Wave Trend 519CD85E36890F5427642B66A5722D15E1DBB0397E0B79C445CACA30A00C0210
Wave Trend 2 3C5AA9C53D471CF2AEE8A5ADBED06F7D9F60B018B4850A5DAE5059E9902EDFF5
WaveTrend Cross Bear 561109E8078015BEA43E165842AEEED0A61ECF89ECC88F2AE8B1043E69EF83F8
WaveTrend Cross Bull E07F2A91BA0322550EC7E36C6404B072D167088130F74A589F35613C7F186AFB
""";

    [Fact]
    public void Every_output_array_matches_the_pre_refactor_fingerprint()
    {
        var cases = new[]
        {
            (Name: "defaults", Parameters: new Dictionary<string, object>(), Expected: DefaultsFingerprint),
            (Name: "WTSignalPeriod=4", Parameters: new Dictionary<string, object> { ["WTSignalPeriod"] = 4.0 }, Expected: SignalFourFingerprint),
            (Name: "AnchorMultiplier=2", Parameters: new Dictionary<string, object> { ["AnchorMultiplier"] = 2.0 }, Expected: DefaultsFingerprint),
        };

        var mismatches = new List<string>();
        foreach (var testCase in cases)
        {
            var actual = FingerprintAll(Calculate(testCase.Parameters));
            if (!string.Equals(testCase.Expected.Trim(), actual, StringComparison.Ordinal))
                mismatches.Add($"{testCase.Name}:\n{actual}");
        }

        Assert.True(mismatches.Count == 0, "Cipher B output fingerprints differ:\n\n" + string.Join("\n\n", mismatches));
    }

    private static Dictionary<string, double[]> Calculate(Dictionary<string, object> parameters)
    {
        var bars = CipherAProviderTests.RawBars();
        var results = new Dictionary<string, double[]>();
        new CipherBProvider().Calculate("CIPHER_B", System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bars),
            parameters, new IndicatorResultBuffer(results, bars.Count));
        return results;
    }

    private static string FingerprintAll(Dictionary<string, double[]> outputs) =>
        string.Join("\n", outputs.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key} {Fingerprint(pair.Value)}"));

    private static string Fingerprint(double[] values)
    {
        var bytes = new byte[values.Length * sizeof(long)];
        for (int i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(i * sizeof(long)), BitConverter.DoubleToInt64Bits(values[i]));
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
