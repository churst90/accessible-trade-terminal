# Accessible Trader — Indicator Plugin Authoring Guide

This document is the definitive reference for writing indicator providers for Accessible Trader.
It covers the full contract: visual rendering, audio sonification, and TTS speech feedback.
All APIs described here are taken directly from the current source code.

---

## Table of Contents

1. [Overview](#1-overview)
2. [IIndicatorProvider Interface](#2-iindicatorprovider-interface)
3. [IndicatorMetadata](#3-indicatormetadata)
4. [IndicatorComponentMetadata — Full Field Reference](#4-indicatorcomponentmetadata--full-field-reference)
5. [Writing Calculate() and UpdateLast()](#5-writing-calculate-and-updatelast)
6. [GetDefaultLevels()](#6-getdefaultlevels)
7. [GetComponentSpeech()](#7-getcomponentspeech)
8. [Known Limitations](#8-known-limitations)
9. [Quick-Start Example](#9-quick-start-example)

---

## 1. Overview

### What Is an Indicator Plugin?

An indicator plugin is a class that implements `IIndicatorProvider` from the
`AccessibleTrader.Sdk` assembly (`AccessibleTrader.Sdk.Interfaces` namespace).

The design goal is **zero Core changes for a new indicator**. A provider declares
all of its behavior — visual appearance, audio character, speech templates, reference
levels, cloud fills, zone bands — entirely within its metadata. Once the metadata is
correct, the rendering engine, sonification engine, and TTS system all pick it up
automatically.

### The Three Output Channels

Every indicator component participates in up to three channels simultaneously:

| Channel | What it does | Where it's controlled |
|---|---|---|
| **Visuals** | Renders lines, histograms, dots, clouds, fills | `ComponentDisplayType`, color fields, `DefaultCloudFills`, `DefaultZoneBands` |
| **Audio** | Sonification during chart playback and navigation | Waveform, envelope, frequency, patch, layer fields |
| **Speech** | TTS announced on navigation (Ctrl+Arrow, Up/Down) | `SpeechTemplate`, `DefaultSignalSpeechTemplate`, `GetComponentSpeech()`, `GetDetailFact()` |

### Where Providers Live

**Built-in providers** live in `AccessibleTrader.Core/Services/Indicators/` and are
registered manually in `ServiceCollectionExtensions.AddIndicatorPipeline()`:

```csharp
services.AddSingleton<IIndicatorProvider, CipherBProvider>();
services.AddSingleton<IIndicatorProvider, CipherSrProvider>();
// etc.
```

**Third-party / external indicator plugins** are auto-discovered at startup from:

1. `{BaseDir}/Plugins/Indicators/` — the built-in indicators plugin directory.
2. `%LOCALAPPDATA%\AccessibleTrader\Plugins\Indicators\` — the user drop-in folder.

The loader scans for DLLs matching the pattern `AccessibleTrader.Plugins.*.dll` using
`Assembly.LoadFrom` inside an isolated `AssemblyLoadContext`. Any concrete class in
those DLLs that implements `IIndicatorProvider` is instantiated via
`Activator.CreateInstance` and registered into the `IIndicatorService` provider list.

### Directory Structure

```
Plugins/
  Providers/    ← Data providers (see PROVIDER_AUTHORING.md)
  Analytics/    ← Analytics data providers
  Indicators/   ← Drop-in indicator plugins (this guide)
```

**Plugin DLL naming convention:** `AccessibleTrader.Plugins.<YourName>.dll`

**Important:** The loader calls `Activator.CreateInstance(type)` with no arguments.
Your provider class must have a public parameterless constructor. You cannot inject
services via the DI container from an external plugin DLL.

---

## 2. IIndicatorProvider Interface

Namespace: `AccessibleTrader.Sdk.Interfaces`

```csharp
public interface IIndicatorProvider
{
    string Name { get; }
    List<IndicatorMetadata> GetIndicators();
    void Calculate(string code, ReadOnlySpan<Ohlcv> data,
        Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
    void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
        Dictionary<string, object> parameters, IIndicatorResultBuffer buffer);
    int GetStabilityWindow(string code, Dictionary<string, object> parameters);
    string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
        IReadOnlyDictionary<string, double[]> calculatedResults, int index,
        Dictionary<string, object> parameters);

    // Default implementations — override as needed:
    string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
        IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        => null;

    List<LevelDescriptor> GetDefaultLevels(string code)
        => new();
}
```

### `string Name { get; }`

A unique string identifier for this provider. Use a short, human-readable name like `"Cipher B"` or `"My Custom RSI"`.

```csharp
public string Name => "Cipher B";
```

This name is used only for diagnostic logging. It does not need to match any indicator
code. Multiple indicators can be exposed by a single provider.

---

### `List<IndicatorMetadata> GetIndicators()`

Returns every indicator this provider exposes. Each `IndicatorMetadata` object fully
describes one indicator: its display name, code, category, parameters, components,
cloud fills, zone bands, and default pane assignment.

The `IIndicatorService` aggregates all providers and exposes the combined list to the
Add Indicator UI.

---

### `void Calculate(...)`

Full calculation over the entire history. Called when:
- The indicator is first added to the chart.
- The symbol or timeframe changes.
- `RequiresFullRecalcOnTick = true` and a new tick arrives.

```csharp
void Calculate(
    string code,                        // matches IndicatorMetadata.Code
    ReadOnlySpan<Ohlcv> data,           // full price history, index 0 = oldest
    Dictionary<string, object> parameters, // user-set parameter values
    IIndicatorResultBuffer buffer       // write results here
)
```

`data` is passed as `ReadOnlySpan<Ohlcv>` for zero-allocation performance.
`Ohlcv` has `Open`, `High`, `Low`, `Close`, `Volume` (all `double`) and `Timestamp` (`DateTime`).

Write every component value to `buffer` using the component's `Name` as the key
(see [Section 5](#5-writing-calculate-and-updatelast)).

---

### `void UpdateLast(...)`

Incremental update for the **final bar only**. Called on every live price tick when
`RequiresFullRecalcOnTick` is false. The signature is identical to `Calculate`.

For simple indicators, `UpdateLast` can delegate to `Calculate`:

```csharp
public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
    Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    => Calculate(code, data, parameters, buffer);
```

For performance-critical indicators with many bars, implement a true incremental path
that only writes to `buffer.SetValue(componentName, data.Length - 1, value)` for the
last index.

---

### `int GetStabilityWindow(...)`

Returns the number of warm-up bars the indicator needs before its output is
meaningful. The rendering and navigation systems use this to gray out or skip
leading bars in the series.

Return a value based on the longest lookback in your calculation:

```csharp
public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
{
    int period = GetInt(parameters, "Period", 14);
    return period * 3; // e.g. 3× the main period as a safe margin
}
```

---

### `string GetDetailFact(...)`

Returns a multi-sentence human-readable description of the indicator's full state at
a specific bar. Announced when the user presses Ctrl+Shift+D (or F4 on some
configurations).

```csharp
string GetDetailFact(
    string code,
    ReadOnlySpan<Ohlcv> data,
    IReadOnlyDictionary<string, double[]> calculatedResults, // all component arrays
    int index,                                               // bar index being described
    Dictionary<string, object> parameters
)
```

`calculatedResults` is keyed by component `Name` (the internal key, not `DisplayName`).
Return `string.Empty` if you have nothing useful to say at that bar.

Example from `CipherSrProvider`:

```csharp
public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
    IReadOnlyDictionary<string, double[]> results, int index,
    Dictionary<string, object> parameters)
{
    double resZone = GetVal(results, CompResistanceLine, index);
    double supZone = GetVal(results, CompSupportLine,    index);
    var sb = new StringBuilder();
    if (!double.IsNaN(resZone)) sb.Append($"Nearest resistance: {resZone:F0}. ");
    if (!double.IsNaN(supZone)) sb.Append($"Nearest support: {supZone:F0}. ");
    return sb.Length > 0 ? sb.ToString().TrimEnd() : "No confirmed S/R levels yet.";
}
```

---

### `string? GetComponentSpeech(...)`

Returns a concise, context-aware speech string for a single component at a given bar.
Called during keyboard navigation when the user moves to a bar while focused on this
indicator's component.

This is a **default interface method** that returns `null` — override it only when
you need runtime context. See [Section 7](#7-getcomponentspeech) for full details.

---

### `List<LevelDescriptor> GetDefaultLevels(string code)`

Returns the reference level lines to inject when the indicator is first added.
This is also a **default interface method** that returns an empty list.
See [Section 6](#6-getdefaultlevels) for full details.

> **Signature warning:** the return type is `List<LevelDescriptor>`
> (`AccessibleTrader.Sdk.Models`), **not** a list of name/value/color tuples as
> older revisions of this document showed. Because this is a default interface
> method, a tuple-returning method with the same name still *compiles* — it just
> declares an unrelated method, the empty-list default wins, and your levels
> silently never draw and never sound.

---

## 3. IndicatorMetadata

Namespace: `AccessibleTrader.Sdk.Models`

`IndicatorMetadata` is the top-level descriptor for one indicator exposed by a provider.

```csharp
public class IndicatorMetadata
{
    public string Name { get; set; }                        // display name in Add Indicator UI
    public string Code { get; set; }                        // unique string ID; passed to Calculate()
    public string Category { get; set; }                    // groups indicators in the UI
    public string Description { get; set; }                 // tooltip / screen reader description
    public bool RequiresFullRecalcOnTick { get; set; }      // default: false
    public List<IndicatorParameterMetadata> Parameters { get; set; }
    public List<IndicatorComponentMetadata> Components { get; set; }
    public List<CloudFillConfig> DefaultCloudFills { get; set; }
    public List<ZoneBandConfig> DefaultZoneBands { get; set; }
    public string DefaultPane { get; set; }                 // default: "Main"
}
```

### `Code`

The string passed as the first argument to `Calculate()`, `UpdateLast()`,
`GetStabilityWindow()`, `GetDetailFact()`, and `GetDefaultLevels()`. Must be unique
across all providers. Convention: `UPPER_SNAKE_CASE`.

```csharp
Code = "CIPHER_B"
Code = "MY_MOMENTUM_OSC"
```

### `DefaultPane`

Where the indicator renders by default:

| Value | Effect |
|---|---|
| `"Main"` | Overlaid on the price candle pane |
| `"Volume"` | Placed in the volume sub-pane |
| Any other string | A new named pane is created (e.g. `"Pane_CIPHER_B"`) |

Each unique non-`"Main"`, non-`"Volume"` pane string creates a distinct indicator
pane beneath the price chart. Multiple indicators can share a pane by using the same
string.

### `RequiresFullRecalcOnTick`

When `true`, the data pipeline calls `Calculate()` on every live tick instead of
`UpdateLast()`. Required for any indicator whose calculation reads historical bar data
at indices other than the last bar — pivot detectors, divergence scanners, any
indicator that writes values back to past indices.

```csharp
RequiresFullRecalcOnTick = true  // CipherSrProvider: writes to past pivot-bar indices
```

### `Parameters`

Each `IndicatorParameterMetadata` describes one user-adjustable parameter:

```csharp
public class IndicatorParameterMetadata
{
    public string Name { get; set; }        // key used in the parameters dictionary
    public string DisplayName { get; set; } // shown in the Parameters dialog
    public string Description { get; set; } // tooltip
    public Type DataType { get; set; }      // typeof(int), typeof(double), typeof(bool)
    public object DefaultValue { get; set; }// must be castable to DataType
}
```

Parameter values arrive in `Calculate()` as `Dictionary<string, object>`. Cast them
carefully — values are often stored as `double` even for integer parameters:

```csharp
int period = parameters.TryGetValue("Period", out var v) && v != null
    ? (int)(double)v : 14;
```

### `DefaultCloudFills`

A list of `CloudFillConfig` objects that define Ichimoku-style ribbon fills between
two named components. Cloud fills are purely visual — they do not appear in the
component navigation tree, cannot be individually focused, and do not generate
speech. They can optionally carry audio via `CloudSonificationConfig`.

```csharp
public class CloudFillConfig
{
    public string UpperComponentName { get; set; } // component Name of the upper boundary
    public string LowerComponentName { get; set; } // component Name of the lower boundary
    public string BullishColorHex { get; set; }    // color when upper >= lower
    public string BearishColorHex { get; set; }    // color when lower > upper
    public bool IsVisible { get; set; }
    public string DisplayName { get; set; }
    public CloudSonificationConfig? Sonification { get; init; } // null = no cloud audio
}

// CloudSonificationConfig is a record:
public record CloudSonificationConfig(
    float BullishFrequency,  // Hz for bullish cloud voice
    float BearishFrequency,  // Hz for bearish cloud voice
    string SoundPatchId,     // patch name from SoundPatchRegistry
    int DecayMs,             // voice duration per bar in ms
    float MaxVolume = 0.85f  // volume at maximum cloud thickness
);
```

Cloud audio fires only during Chart-scope playback (Space key), not during navigation.

### `DefaultZoneBands`

A list of `ZoneBandConfig` objects defining thin horizontal shaded bands around a
carry-forward level component. Used for S/R zone shading (e.g. ±0.3% around a
resistance price). Zone bands are purely visual.

```csharp
public class ZoneBandConfig
{
    public string ComponentName { get; set; }  // component whose value is the band centre
    public string ColorHex { get; set; }       // #RRGGBB or #AARRGGBB (alpha-first 8-hex)
    public float BandWidthPct { get; set; }    // half-width as % of price (e.g. 0.3 = ±0.3%)
    public bool IsVisible { get; set; }
    public string DisplayName { get; set; }
}
```

The `ComponentName` must match the `Name` field of a component in the same indicator.
The band centre tracks whatever value that component holds at each bar.

---

## 4. IndicatorComponentMetadata — Full Field Reference

Namespace: `AccessibleTrader.Sdk.Models`

Each entry in `IndicatorMetadata.Components` declares one data channel (line, dot,
histogram, etc.). The system reads these fields when the indicator is first added and
applies them to the corresponding `ComponentConfig`. All `Default*` fields are
nullable — `null` means "defer to the global role/type-based default."

### Identity

| Field | Type | Description |
|---|---|---|
| `Name` | `string` | **Internal key.** Must match exactly the key passed to `IIndicatorResultBuffer.GetComponentSpan()` or `SetValue()` in your `Calculate()`. This is the primary source of key-mismatch bugs. |
| `DisplayName` | `string?` | User-facing label shown in the UI, spoken in navigation. Defaults to `Name` when null. |
| `Role` | `ComponentRole` | Semantic role — drives default sonification routing (see table below). |
| `DisplayType` | `ComponentDisplayType` | How the component renders visually (see table below). |
| `IsVisible` | `bool` | Default visibility. `false` = component is hidden until the user enables it. Default: `true`. |
| `DataMapping` | `string?` | For price-series components mapped to OHLCV fields (`"Open"`, `"High"`, `"Low"`, `"Close"`, `"Volume"`). Leave null for all calculated indicator components. |

#### ComponentRole values

| Value | Meaning |
|---|---|
| `None` | No special semantic role — generic default routing. |
| `PriceAction` | Main price data (candle body, close line). |
| `Body` | Candle body fill. |
| `Wick` | Candle wick / shadow. |
| `Median` | Midline of a band or channel. |
| `UpperBand` | Upper boundary of a band (Bollinger, Keltner). |
| `LowerBand` | Lower boundary of a band. |
| `Level` | Carry-forward horizontal level (S/R zone lines). Triggers zone proximity cue via `IsZoneLine`. |
| `Signal` | Discrete event signal (crossover dots, divergence markers). Uses Ping envelope by default. |
| `Histogram` | Discrete per-bar histogram column. |
| `Volume` | Volume bar. |
| `Boundary` | Hard boundary line (e.g. chart edge or zero floor). |

#### ComponentDisplayType values

| Value | Visual output |
|---|---|
| `Line` | Continuous line connecting bar values. |
| `Bar` | Vertical bar anchored at zero. |
| `Histogram` | Discrete per-bar bars anchored at a reference level. |
| `Oscillator` | Like Line but semantically an oscillator; auto-applies `ReferenceLevel = 0`. |
| `Dot` | Small filled circle at the value Y position. Supports Ctrl+Left/Right sparse navigation. |
| `Arrow` | Up or down triangle; direction set by value sign. |
| `TriangleUp` | Fixed upward-pointing triangle (pre-determined direction). |
| `TriangleDown` | Fixed downward-pointing triangle. |
| `Diamond` | Rotated 45° square — visually distinct from Dot. |
| `Square` | Axis-aligned filled square. |
| `Cross` | X-shaped marker. |
| `StepLine` | Stepped / staircase line — horizontal at current value, drops to next. |
| `Cloud` | Fill region between two named components. Set `UpperComponentName` and `LowerComponentName`. |
| `GradientDot` | Dot whose color is driven by a companion `{Name}_color` array. |
| `ZeroArea` | Wave line with dual-color fill: green above zero, red below. |
| `ZeroDot` | Dot fixed at the zero-line Y position, colored green/red by value sign. |
| `Area` | Line with shaded fill down to the pane bottom. |
| `Gradient` | Line with gradient fill to pane bottom (area-chart style). |
| `Level` | Static horizontal line at a fixed Y value. |
| `Wick`, `Candle`, `Profile`, `Heatmap`, `Distribution` | Specialized internal types; not typically used by indicator plugins. |

---

### Visual

| Field | Type | Description |
|---|---|---|
| `DefaultColorHex` | `string?` | Primary color (bullish / positive side). Hex string: `"#RRGGBB"` or `"#AARRGGBB"`. |
| `DefaultColorHexSecondary` | `string?` | Secondary color (bearish / negative side) for polarity-colored components. |
| `DefaultThickness` | `float?` | Stroke / shape size in logical pixels. |
| `DefaultDashStyle` | `DashStyle?` | Line dash pattern: `Solid`, `Dash`, `Dot`, or `DashDot`. |
| `ColorBaseline` | `double?` | Value at which directional bar / histogram coloring switches. Default `0`. Set to `50` for MFI (midpoint of 0–100). Set to `-80` for a histogram anchored at −80. |
| `DefaultColorSource` | `ColorSource?` | `Value` = color by value sign vs baseline. `PriceAction` = color by candle direction. |
| `DefaultIsAreaFill` | `bool?` | Whether to shade the region between the line and its reference level. Null defers to StylingService. |
| `DefaultUsePolarityColoring` | `bool?` | Whether color flips above/below the reference level. Null defers to StylingService. |
| `DefaultColorRules` | `List<ColorRule>?` | Per-bar conditional color rules. Applied in order — first matching rule wins. Null or empty = use `DefaultColorHex` for all bars (no conditional coloring). Rules are copied into `ComponentConfig.ColorRules` by `IndicatorModelFactory` at component creation time. |

#### Declaring DefaultColorRules

`ColorRule` is a record with three fields:

```csharp
public record ColorRule
{
    public required ColorCondition Condition { get; init; }
    public required string ColorHex { get; init; }
    public double Level { get; init; } = 0.0; // threshold for AboveLevel / BelowLevel conditions
}
```

Available `ColorCondition` values:

| Value | Fires when |
|---|---|
| `AboveZero` | Component value ≥ 0 |
| `BelowZero` | Component value < 0 |
| `Rising` | Value increased vs previous bar |
| `Falling` | Value decreased vs previous bar |
| `AboveLevel` | Value > `Level` threshold |
| `BelowLevel` | Value ≤ `Level` threshold |

Example — a histogram that is green when rising, red when falling:

```csharp
new IndicatorComponentMetadata
{
    Name        = "MACD Histogram",
    DisplayType = ComponentDisplayType.Histogram,
    DefaultColorRules = new List<ColorRule>
    {
        new() { Condition = ColorCondition.Rising,  ColorHex = "#4CAF50" }, // green
        new() { Condition = ColorCondition.Falling, ColorHex = "#F44336" }, // red
    }
}
```

Rules are evaluated in declaration order; the first matching rule wins. A bar that
matches no rule falls back to `DefaultColorHex`.

---

### Sub-Panes

A sub-pane is a horizontal strip within the parent indicator pane. Components in the
same sub-pane share a contiguous vertical region at the bottom of the pane.

| Field | Type | Description |
|---|---|---|
| `SubPaneName` | `string?` | String key for the sub-pane strip. All components with the same key share one sub-pane. Null = component renders in the main area of its pane (default). |
| `SubPaneHeightRatio` | `float?` | Height of the sub-pane as a fraction of the parent pane height. Clamped to `[0.05, 0.40]` at render time. Example: `0.22f` = sub-pane occupies 22%, main area gets 78%. Only meaningful when `SubPaneName` is set. |

---

### Audio

All audio fields are nullable; null means "use the profile / display-type default."

| Field | Type | Description |
|---|---|---|
| `DefaultWaveform` | `string?` | Oscillator waveform: `"sine"`, `"square"`, `"triangle"`, `"sawtooth"`, `"noise"`. |
| `DefaultAboveWaveform` | `string?` | Waveform when value is above the reference level. Overrides `DefaultWaveform` for the positive region. |
| `DefaultBelowWaveform` | `string?` | Waveform when value is below the reference level. |
| `DefaultEnvelopeType` | `string?` | `"Sustain"` = continuous gliding tone (for lines and oscillators). `"Ping"` = transient click with decay (for marker dots and discrete signals). |
| `DefaultDecayMs` | `int?` | For Ping envelope: how long the sound rings in milliseconds. Longer = more ringing. |
| `DefaultBaseFrequency` | `double?` | Base oscillator frequency in Hz. Default: 440 Hz. |
| `DefaultBullishFrequency` | `double?` | Pitch in Hz for the bullish / positive direction. Used when `DefaultPitchMapping = Direction`. |
| `DefaultBearishFrequency` | `double?` | Pitch in Hz for the bearish / negative direction. |
| `DefaultFreqMultiplier` | `double?` | Multiplier applied to the base pitch. `1.3` = 30% higher. Default: `1.0`. |
| `DefaultNoiseAmount` | `float?` | Pink-noise blend: `0.0` = pure waveform, `1.0` = pure noise. Values around `0.15` add subtle texture. |
| `DefaultAmplitudeMapping` | `AmplitudeMapping?` | How data values drive voice amplitude (see below). |
| `DefaultPitchMapping` | `PitchMapping?` | How data values drive voice pitch (see below). |
| `DefaultPlaybackLayer` | `PlaybackLayer?` | Volume tier during multi-series playback (see below). |
| `DefaultSoundPatchId` | `string?` | Named patch from `SoundPatchRegistry`. Overrides individual waveform/envelope fields. Available patches: `"bell"`, `"crystal_bell"`, `"dual_tone_bell"`, `"triangle_bell"`, `"sine_bell"`. Null = use per-field settings. |
| `DefaultTriggerBoundaryClick` | `bool?` | When true, a transient click earcon fires each time the value crosses any visible reference level line. |

#### AmplitudeMapping values

| Value | Effect |
|---|---|
| `None` | Constant amplitude regardless of value. |
| `Absolute` | Amplitude scales with the absolute value. |
| `ReferenceDeviation` | Amplitude scales with distance from the reference level. |
| `Size` | Amplitude scales with the rendered shape size (for marker dots). |
| `DeltaFromPrice` | Amplitude scales with distance from the current price. |

#### PitchMapping values

| Value | Effect |
|---|---|
| `None` | Fixed pitch at `BaseFrequency`. |
| `Value` | Pitch scales with the component's absolute value within the viewport range. |
| `Direction` | Pitch is `BullishFrequency` or `BearishFrequency` based on value sign relative to the reference level. |
| `PriceDirection` | Pitch is bullish/bearish based on the bar's candle direction (close vs open). |
| `Price` | Pitch maps the bar's absolute price position within the viewport (200–1000 Hz). |

#### PlaybackLayer values

| Value | Volume during playback | Typical use |
|---|---|---|
| `Background` | 60% | Continuous context waves, hidden anchor lines. |
| `Midground` | 80% | Default for most lines and oscillators. |
| `Foreground` | 100% | Discrete signal events that must cut through (dots, bells). |

---

### Speech

| Field | Type | Description |
|---|---|---|
| `SpeechTemplate` | `string?` | Template for continuous line/oscillator speech. Tokens: `{value}`, `{value:F1}`, `{value:F2}`, `{name}`, `{date}`. Used when no `GetComponentSpeech()` override is active. |
| `DefaultSignalSpeechTemplate` | `string?` | Template for marker/dot components when a non-NaN signal IS present. Tokens: `{price}` (formats the value as an integer price), `{name}`. Returns empty string (no speech) when value IS NaN. Takes precedence over `SpeechTemplate` for Dot/Arrow/Diamond/Square/Cross/TriangleUp/TriangleDown/ZeroDot display types. |
| `UsesGradientSpeech` | `bool` | When true, navigation speech produces qualitative momentum language — "strong bullish momentum", "neutral momentum", etc. — rather than a raw numeric value. Intended for `GradientDot` components whose value is more meaningful as a direction + intensity description. Default: `false`. |

---

### Levels and Boundaries

| Field | Type | Description |
|---|---|---|
| `DefaultReferenceLevel` | `double?` | Explicit reference level for this component. Drives the waveform above/below split, polarity coloring boundary, and `TriggerBoundaryClick` threshold. When null, the engine uses `0.0` for `Oscillator` and `ZeroArea` display types, otherwise null. |
| `DefaultTriggerBoundaryClick` | `bool?` | See Audio section above. |
| `DefaultIsZoneLine` | `bool` | When true, `NavigationFeedbackManager` plays a quiet 100ms proximity tone on audio slot 2 whenever the navigated bar's price range (High/Low) overlaps this component's value within 0.5% tolerance. Intended for carry-forward S/R lines (Resistance Zone, Support Zone). Propagated to `ComponentConfig.IsZoneLine`. Default: `false`. |

---

### Cloud Fill Boundaries (on Cloud display type components)

When a component uses `DisplayType = ComponentDisplayType.Cloud`, set these on the
**component config itself** (not on `CloudFillConfig`) to identify which two
components bound the fill:

| Field | Type | Description |
|---|---|---|
| `UpperComponentName` | `string?` | `Name` of the component forming the upper fill boundary. |
| `LowerComponentName` | `string?` | `Name` of the component forming the lower fill boundary. |

For most cloud fills, prefer `DefaultCloudFills` on `IndicatorMetadata` — that is
the cleaner pattern used by all built-in indicators.

---

## 5. Writing Calculate() and UpdateLast()

### IIndicatorResultBuffer

The buffer is the output channel for all component data:

```csharp
public interface IIndicatorResultBuffer
{
    // Returns a writable Span<double> for a component — one slot per bar.
    // If the component has not been written before, a new array of the correct
    // length is allocated. Calling this for a component name NOT in your metadata
    // is legal but the data will be invisible to the rendering and speech systems.
    Span<double> GetComponentSpan(string componentName);

    // Sets a single value at the given bar index. Used for incremental UpdateLast()
    // when you want to write only the last bar without touching the rest.
    void SetValue(string componentName, int index, double value);

    // Writes dynamic zone bands computed from bar data. Call this from Calculate()
    // when your provider derives S/R zone definitions at runtime (e.g. from pivot prices).
    // IndicatorOrchestrator reads these after Calculate() returns and applies them to
    // series.Config.ZoneBands, replacing any previously-written dynamic bands.
    // Pass an empty list to clear all dynamic zones for this indicator.
    void WriteZoneBands(string indicatorCode, List<ZoneBandConfig> zoneBands);
}
```

### Writing Dynamic Zone Bands

Use `buffer.WriteZoneBands()` when your indicator computes S/R zone band positions at
runtime — that is, when the band centres are derived from the bar data rather than
being fixed in metadata.

```csharp
public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
    Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
{
    // ... compute pivot levels ...
    double resistanceLevel = /* computed from bar data */;
    double supportLevel    = /* computed from bar data */;

    // Write dynamic zone bands — these replace series.Config.ZoneBands after Calculate() returns.
    buffer.WriteZoneBands(code, new List<ZoneBandConfig>
    {
        new() { ComponentName = "Resistance Zone", ColorHex = "#40CE93D8",
                BandWidthPct = 0.3f, DisplayName = "Resistance Band", IsVisible = true },
        new() { ComponentName = "Support Zone",    ColorHex = "#40FFD700",
                BandWidthPct = 0.3f, DisplayName = "Support Band",    IsVisible = true },
    });
}
```

The `indicatorCode` passed to `WriteZoneBands` must match the `code` parameter passed
to `Calculate()` (case-insensitive). The orchestrator uses this key to route the bands
to the correct series.

For static zone bands that do not change with bar data, continue to use
`IndicatorMetadata.DefaultZoneBands` — no `WriteZoneBands()` call is needed.

### Avoiding key mismatch bugs

The buffer is matched to components by `component.Name` **exactly** (case-sensitive).
A typo in the key passed to `GetComponentSpan()` or `SetValue()` produces an array
that is never rendered or spoken — silent NaN data with no error thrown.

**Best practice:** declare every component name as a `public const string` in your
provider class and reference those constants in both your metadata declarations and
your buffer write calls. This pattern is used by all built-in providers:

```csharp
public class MyProvider : IIndicatorProvider
{
    // Declare once — typos become compile errors.
    public const string CompLine   = "My Line";
    public const string CompSignal = "My Signal";

    public List<IndicatorMetadata> GetIndicators() => new()
    {
        new IndicatorMetadata
        {
            Components = new List<IndicatorComponentMetadata>
            {
                new() { Name = CompLine,   DisplayType = ComponentDisplayType.Line   },
                new() { Name = CompSignal, DisplayType = ComponentDisplayType.Dot    },
            }
        }
    };

    public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
        Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
    {
        // Use the constant — no string literal here.
        var span = buffer.GetComponentSpan(CompLine);
        // ...
        buffer.SetValue(CompSignal, someBar, data[someBar].Close);
    }
}
```

A **runtime warning** is logged in Debug builds when a key written to the buffer has
no matching `ComponentConfig.Name` in the series. The warning message is:

```
[ProviderName] wrote buffer key 'SomeKey' for indicator 'MY_CODE' but no component
with that Name exists in the series. Data will be silently ignored. Check that
buffer.Write() key matches the component Name in metadata.
```

Intentional companion arrays (keys ending in `_color` or `_touches`, and keys
starting with `__`) are excluded from this check.

### The double[] convention

- Write one value per bar, `data.Length` values total.
- Index 0 is the oldest bar; `data.Length - 1` is the most recent.
- Use `double.NaN` for bars where a signal is absent (e.g. sparse marker dots) or
  the component is still in its warm-up period.
- Never leave a span partially unwritten — uninitialized memory will contain garbage.
  Either fill with `NaN` first or write every slot.

```csharp
// Pattern: allocate, fill with NaN, then set valid bars
var mySignal = new double[n];
Array.Fill(mySignal, double.NaN);
// ... compute logic ...
mySignal[someBar] = someValue;

// Write to buffer
var span = buffer.GetComponentSpan("My Signal");
for (int i = 0; i < Math.Min(span.Length, n); i++)
    span[i] = mySignal[i];
```

Alternatively, get the span directly and write into it:

```csharp
var span = buffer.GetComponentSpan("My Signal");
span.Fill(double.NaN);
span[someBar] = someValue;
```

### Companion Arrays

**GradientDot** components require a companion array for per-bar color interpolation:

```csharp
// Main component: Y position (e.g. close price)
buffer.GetComponentSpan("WT Momentum").Fill( /* close price at each bar */ );

// Companion: raw oscillator value driving the color gradient (teal→gray→red)
buffer.GetComponentSpan("WT Momentum_color").Fill( /* oscillator value at each bar */ );
```

The companion key is always `{componentName}_color`.

**Touch-count** companions (used in CipherSrProvider) follow the same pattern — the
companion is written to the buffer but no matching `IndicatorComponentMetadata` is
declared for it. It exists purely for `GetComponentSpeech` to read:

```csharp
buffer.GetComponentSpan("Resistance_touches") // accessed in GetComponentSpeech, not in metadata
```

### RequiresFullRecalcOnTick

Set `RequiresFullRecalcOnTick = true` in `IndicatorMetadata` when your `Calculate()`
writes to historical bar indices — not just to the last bar. Pivot detectors,
divergence scanners, and carry-forward zone line indicators need this. When set, the
engine routes every live tick through `Calculate()` rather than `UpdateLast()`.

### Minimal Skeleton

```csharp
public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
    Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
{
    if (!code.Equals("MY_CODE", StringComparison.OrdinalIgnoreCase)) return;
    int n = data.Length;
    if (n < 2) return;

    int period = GetInt(parameters, "Period", 14);

    // 1. Allocate output arrays and fill with NaN.
    var lineOut   = new double[n];
    var signalOut = new double[n];
    Array.Fill(lineOut,   double.NaN);
    Array.Fill(signalOut, double.NaN);

    // 2. Your math here.
    for (int i = period; i < n; i++)
    {
        lineOut[i] = /* computed value */;
        if (/* signal condition */)
            signalOut[i] = data[i].Close; // dot placed at close price
    }

    // 3. Write to buffer. Key must match component Name exactly.
    WriteSpan(buffer, "My Line",   lineOut,   n);
    WriteSpan(buffer, "My Signal", signalOut, n);
}

private static void WriteSpan(IIndicatorResultBuffer buffer,
    string name, double[] data, int n)
{
    var span = buffer.GetComponentSpan(name);
    int len = Math.Min(span.Length, n);
    for (int i = 0; i < len; i++) span[i] = data[i];
}

private static int GetInt(Dictionary<string, object> p, string key, int def)
    => p.TryGetValue(key, out var v) && v != null
       ? (int)(double)v : def;
```

---

## 6. GetDefaultLevels()

`GetDefaultLevels` returns horizontal reference level lines injected into the series
immediately when the indicator is added. The user sees these as dashed or dotted
threshold lines on the chart.

```csharp
List<LevelDescriptor> GetDefaultLevels(string code)
```

`LevelDescriptor` (in `AccessibleTrader.Sdk.Models`) is a record:

```csharp
public record LevelDescriptor(
    string Name,
    double Value,
    string ColorHex,
    DashStyle Dash,
    bool PlayEarcon       = false,   // earcon when the series crosses this level
    float EarconVolume    = 0.7f,
    float ZoneNoiseAmount = 0f,      // background noise while inside the zone
    string ZoneNoiseType  = "pink"
);
```

The audio fields default to off, so declare only what you use — but remember that
for a blind user the earcons and zone noise are the levels: a line that only draws
conveys nothing.

Return an empty list if your indicator has no meaningful threshold levels.

`SeriesManagementService.InjectDefaultLevels()` calls this method when the series
is created; there is **no static fallback table** — if you return an empty list,
the indicator simply has no reference levels. User-supplied `Overbought` /
`Oversold` parameter values override the `Value` of descriptors with those exact
names, so name your threshold levels `"Overbought"` and `"Oversold"` if you want
them user-tunable.

### DashStyle options

| Value | Appearance |
|---|---|
| `DashStyle.Solid` | Solid line |
| `DashStyle.Dash` | Standard dashes |
| `DashStyle.Dot` | Dots |
| `DashStyle.DashDot` | Alternating dash and dot |

### CipherB reference levels (actual values from source)

```csharp
public List<LevelDescriptor> GetDefaultLevels(string code)
{
    if (!code.Equals("CIPHER_B", StringComparison.OrdinalIgnoreCase)) return new();
    return new()
    {
        new("Extreme OB",  60.0, "#FF2222", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
        new("Overbought",  53.0, "#FF6666", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
        new("Zero",         0.0, "#666666", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.7f),
        new("Oversold",   -53.0, "#66BB66", DashStyle.Dash, PlayEarcon: true, EarconVolume: 0.6f, ZoneNoiseAmount: 0.10f, ZoneNoiseType: "white"),
        new("Extreme OS", -60.0, "#22FF22", DashStyle.Dot,  PlayEarcon: true, EarconVolume: 0.8f, ZoneNoiseAmount: 0.20f, ZoneNoiseType: "white"),
    };
}
```

### Multi-indicator providers

If your provider exposes multiple indicators with different levels, switch on `code`:

```csharp
public List<LevelDescriptor> GetDefaultLevels(string code) => code.ToUpperInvariant() switch
{
    "MY_RSI" => new()
    {
        new("Overbought", 70.0, "#FF6666", DashStyle.Dash, PlayEarcon: true),
        new("Oversold",   30.0, "#66BB66", DashStyle.Dash, PlayEarcon: true),
    },
    "MY_MACD" => new()
    {
        new("Zero", 0.0, "#666666", DashStyle.Dash),
    },
    _ => new()
};
```

---

## 7. GetComponentSpeech()

```csharp
string? GetComponentSpeech(
    string componentName,                              // component Name (internal key)
    double value,                                      // component value at dataIndex (may be NaN)
    Ohlcv bar,                                         // OHLCV bar at dataIndex
    IReadOnlyDictionary<string, double[]> allComponentData, // all component arrays for this series
    int dataIndex                                      // absolute bar index being navigated
)
```

### When to implement vs SpeechTemplate

Use **`SpeechTemplate`** (declared in metadata) when the speech for a component is
a simple formatting of its own value. Example: `"Wave Trend 1. Oscillator. {value:F1}."`.

Use **`GetComponentSpeech()`** when you need:
- Values from other components at the same bar (e.g. include the WT1 level when
  announcing a signal dot).
- Conditional language based on runtime values (e.g. "overbought" vs "oversold").
- Context from companion arrays (e.g. touch counts, color gradients).
- Distance calculations relative to live price.

### The three-path speech system

When the user navigates to a bar, the speech system tries paths in order:

1. **Provider override**: `GetComponentSpeech()` is called. If it returns a non-null
   string, that string is used. The `NavigationFeedbackManager` prepends
   `"[DisplayName]. [TypeLabel]. "` to the returned value.
2. **Template**: If `GetComponentSpeech()` returns `null`, the system uses
   `SpeechTemplate` (for lines/oscillators) or `DefaultSignalSpeechTemplate` (for
   marker types) from the component metadata.
3. **Generic fallback**: If both are null/empty, a generic `"{name}, {type}, {value}"`
   string is constructed.

### The `componentName` parameter

This is the internal `Name` field from `IndicatorComponentMetadata`, not the
`DisplayName`. Use your component name constants (which should match your `Name`
declarations exactly) in the switch expression:

```csharp
return componentName switch
{
    CompWT1 => GetWTValueSpeech(value),          // CompWT1 = "Wave Trend"
    CompBlue => GetSignalDotSpeech(bar, allComponentData, dataIndex),
    _ => null  // fall through to SpeechTemplate for anything not explicitly handled
};
```

### The `allComponentData` dictionary

Keyed by component **`Name`** (the internal key). Contains the full data arrays for
every component in this series, including companion arrays that were written to the
buffer but have no corresponding `IndicatorComponentMetadata` entry.

```csharp
// Reading a sibling component's value:
double wt1Value = allComponentData.TryGetValue("Wave Trend", out var arr) && dataIndex < arr.Length
    ? arr[dataIndex] : double.NaN;

// Reading a companion touch-count array:
double touches = allComponentData.TryGetValue("Resistance_touches", out var tc) && dataIndex < tc.Length
    ? tc[dataIndex] : double.NaN;
```

### The `__live_close` injection

`NavigationFeedbackManager` injects the current live (most recent) close price into
`allComponentData` under the key `"__live_close"`. The array has exactly one element:

```csharp
double liveClose = allComponentData.TryGetValue("__live_close", out var lcArr)
    && lcArr != null && lcArr.Length > 0 && lcArr[0] > 0
    ? lcArr[0]
    : bar.Close; // fallback to the navigated bar's close
```

Use `__live_close` for distance calculations in S/R indicators so that "price is X%
below resistance" always reflects the **current** price, not the price at the
historical bar being navigated.

### Return `null` to fall through

Return `null` for any component whose template-based speech is sufficient. Do not
return an empty string to suppress speech — return `null` to fall through to the
template, or return an empty string only if you intentionally want silence:

```csharp
return componentName switch
{
    "My Signal" when !double.IsNaN(value) => $"Signal at {bar.Close:F2}",
    "My Signal" => null,   // NaN = no signal = fall through; template will emit empty via SignalSpeechTemplate
    _ => null              // all other components: use their SpeechTemplate
};
```

### CipherSR example — reading companion arrays and live close

```csharp
public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
    IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
{
    if (double.IsNaN(value)) return "no data";

    double liveClose = allComponentData.TryGetValue("__live_close", out var lcArr)
        && lcArr != null && lcArr.Length > 0 && lcArr[0] > 0
        ? lcArr[0] : bar.Close;
    double distancePct = liveClose > 0
        ? Math.Abs(liveClose - value) / liveClose * 100.0
        : double.NaN;

    return componentName switch
    {
        "Resistance" => $"{value:F2}, price {distancePct:F1}% below",
        "Support"    => $"{value:F2}, price {distancePct:F1}% above",
        _ => null
    };
}
```

---

## 8. Known Limitations

### 1. IIndicatorResultBuffer is string-keyed with no compile-time safety

The binding between a component's `Name` in metadata and the key used in
`buffer.GetComponentSpan(key)` is a runtime string match. Mismatched keys silently
produce arrays that are never populated — the component's data will be all `NaN` with
no error thrown. Use `const string` component name constants and reference those
constants in both your metadata declarations and your buffer write calls (see the
"Avoiding key mismatch bugs" section in [Section 5](#5-writing-calculate-and-updatelast)
for the full pattern). A runtime warning is also logged in Debug builds when a
mismatch is detected.

### 2. Common math helpers are not in AccessibleTrader.Sdk

EMA, SMA, ATR, RSI, and other common technical calculations are not exposed by the
SDK. Each built-in provider contains its own private implementations. External plugin
authors must either:
- Bring their own math implementations.
- Reference a third-party technical analysis library (e.g. Skender.Stock.Indicators)
  and include it in their plugin DLL's output folder.

The `PluginLoadContext` isolates plugin-specific third-party DLLs from the host
application using `AssemblyLoadContext`, so version conflicts are contained.

### 3. No hot-reload for external plugins

Plugin discovery runs once at application startup inside `DataService.InitializeAsync()`.
Adding or replacing a provider DLL in the Plugins directory requires a full application
restart. There is no mechanism to reload plugins at runtime.

### 4. External plugins require parameterless constructors

`PluginLoaderService` instantiates providers via `Activator.CreateInstance(type)` with
no arguments. You cannot inject services (logging, HTTP clients, file system, etc.)
through the DI container. Any configuration or initialization your provider needs must
happen either in the constructor directly or lazily on first `Calculate()` call using
data available from the parameters dictionary.

---

## 9. Quick-Start Example

The following is a minimal but complete provider implementing a momentum indicator
with one line component and one signal dot component. It demonstrates the full
interface, correct buffer writing, reference levels, and speech handling.

```csharp
using System;
using System.Collections.Generic;
using AccessibleTrader.Sdk.Interfaces;
using AccessibleTrader.Sdk.Models;

namespace MyCompany.AccessibleTrader.Plugins.Momentum
{
    /// <summary>
    /// Simple momentum oscillator: normalized rate-of-change + overbought signal dot.
    ///
    /// Components:
    ///   Momentum Line  — continuous oscillator in the range approx. -100 to +100.
    ///   OB Signal      — sparse dot placed at the close price when the oscillator
    ///                    crosses above the overbought threshold.
    ///
    /// Reference levels: +70 (overbought), 0 (zero), -70 (oversold).
    /// </summary>
    public class SimpleMomentumProvider : IIndicatorProvider
    {
        // ── Component name constants ───────────────────────────────────────────
        // Always use constants so metadata and buffer writes stay in sync.
        public const string CompLine   = "Momentum Line";
        public const string CompSignal = "OB Signal";

        public string Name => "MyCompany.SimpleMomentum";

        public List<IndicatorMetadata> GetIndicators() => new()
        {
            new IndicatorMetadata
            {
                Code        = "SIMPLE_MOMENTUM",
                Name        = "Simple Momentum",
                Category    = "Oscillators",
                Description = "Normalized rate-of-change oscillator with overbought signal dots.",

                // A new named pane is created for this indicator.
                DefaultPane = "Pane_SIMPLE_MOMENTUM",

                Parameters = new List<IndicatorParameterMetadata>
                {
                    new()
                    {
                        Name         = "Period",
                        DisplayName  = "Period",
                        Description  = "Lookback period for the rate-of-change calculation.",
                        DataType     = typeof(int),
                        DefaultValue = 14.0,  // stored as double even for int parameters
                    },
                    new()
                    {
                        Name         = "OBLevel",
                        DisplayName  = "Overbought Level",
                        Description  = "Oscillator value above which an overbought dot fires.",
                        DataType     = typeof(double),
                        DefaultValue = 70.0,
                    },
                },

                Components = new List<IndicatorComponentMetadata>
                {
                    // ── Continuous oscillator line ─────────────────────────────────────
                    new()
                    {
                        Name        = CompLine,
                        DisplayName = "Momentum",
                        Role        = ComponentRole.Signal,
                        DisplayType = ComponentDisplayType.Oscillator,
                        IsVisible   = true,

                        // Visual
                        DefaultColorHex          = "#29B6F6",   // light blue
                        DefaultColorHexSecondary = "#EF5350",   // red for negative region
                        DefaultThickness         = 2.0f,
                        DefaultUsePolarityColoring = true,      // flip color at zero

                        // The Oscillator display type auto-applies ReferenceLevel = 0.
                        // Explicitly set it anyway to be self-documenting.
                        DefaultReferenceLevel    = 0.0,

                        // Audio: triangle waveform, pitch tracks the oscillator value.
                        // Above zero = sharp angular sound; below zero = smooth sine descent.
                        DefaultAboveWaveform     = "triangle",
                        DefaultBelowWaveform     = "sine",
                        DefaultPitchMapping      = PitchMapping.Value,
                        DefaultAmplitudeMapping  = AmplitudeMapping.None,
                        DefaultPlaybackLayer     = PlaybackLayer.Midground,

                        // Click when crossing visible level lines (0, ±70).
                        DefaultTriggerBoundaryClick = true,

                        // Speech: simple value format, used when GetComponentSpeech returns null.
                        SpeechTemplate           = "Momentum. Oscillator. {value:F1}.",
                    },

                    // ── Sparse overbought signal dot ───────────────────────────────────
                    new()
                    {
                        Name        = CompSignal,
                        DisplayName = "OB Signal",
                        Role        = ComponentRole.Signal,
                        DisplayType = ComponentDisplayType.Dot,
                        IsVisible   = true,

                        // Visual: bright yellow, large dot
                        DefaultColorHex          = "#FFD600",
                        DefaultThickness         = 6.0f,
                        DefaultUsePolarityColoring = false,

                        // Audio: Ping (transient bell), foreground layer so it cuts through.
                        DefaultEnvelopeType      = "Ping",
                        DefaultDecayMs           = 280,
                        DefaultSoundPatchId      = "crystal_bell",
                        DefaultPitchMapping      = PitchMapping.None,
                        DefaultBaseFrequency     = 880.0,  // high-pitched ceiling alert
                        DefaultAmplitudeMapping  = AmplitudeMapping.None,
                        DefaultPlaybackLayer     = PlaybackLayer.Foreground,

                        // Speech: announced when the dot is present (non-NaN).
                        // Returns empty string when the bar has no signal (NaN).
                        DefaultSignalSpeechTemplate = "Overbought signal at {price}",
                    },
                },
            }
        };

        public void Calculate(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
        {
            if (!code.Equals("SIMPLE_MOMENTUM", StringComparison.OrdinalIgnoreCase)) return;
            int n = data.Length;
            if (n < 2) return;

            // Read parameters. Values arrive as doubles even for integer parameters.
            int    period  = GetInt(parameters, "Period",   14);
            double obLevel = GetDbl(parameters, "OBLevel",  70.0);

            // Allocate output arrays and pre-fill with NaN.
            var lineOut   = new double[n];
            var signalOut = new double[n];
            Array.Fill(lineOut,   double.NaN);
            Array.Fill(signalOut, double.NaN);

            // Simple rate-of-change normalized by the period-ago close.
            for (int i = period; i < n; i++)
            {
                double prev = data[i - period].Close;
                if (prev < 1e-10) continue;  // guard against zero-price bars

                // Scale to approx. -100..+100 range
                lineOut[i] = (data[i].Close - prev) / prev * 100.0;

                // Signal dot: fires when momentum crosses above OB level on this bar
                // and was below it on the previous bar.
                if (i > period &&
                    lineOut[i] >= obLevel &&
                    !double.IsNaN(lineOut[i - 1]) &&
                    lineOut[i - 1] < obLevel)
                {
                    // Dot value = close price (positions the dot at bar's price Y level).
                    signalOut[i] = data[i].Close;
                }
            }

            // Write to buffer. Key must match component Name exactly.
            WriteSpan(buffer, CompLine,   lineOut,   n);
            WriteSpan(buffer, CompSignal, signalOut, n);
        }

        public void UpdateLast(string code, ReadOnlySpan<Ohlcv> data,
            Dictionary<string, object> parameters, IIndicatorResultBuffer buffer)
            // For a simple indicator, delegate full recalculation.
            // For performance-critical cases, implement an incremental path.
            => Calculate(code, data, parameters, buffer);

        public int GetStabilityWindow(string code, Dictionary<string, object> parameters)
        {
            // Report the warmup bars needed before output is meaningful.
            int period = GetInt(parameters, "Period", 14);
            return period + 5; // a few extra bars of safety margin
        }

        public string GetDetailFact(string code, ReadOnlySpan<Ohlcv> data,
            IReadOnlyDictionary<string, double[]> results, int index,
            Dictionary<string, object> parameters)
        {
            double val = GetVal(results, CompLine, index);
            if (double.IsNaN(val)) return "Momentum: no data.";

            double obLevel = GetDbl(parameters, "OBLevel", 70.0);
            string zone = val >= obLevel  ? "overbought"
                        : val <= -obLevel ? "oversold"
                        :                  "neutral";

            return $"Momentum: {val:F1}, {zone}. Close: {data[index].Close:F2}.";
        }

        public string? GetComponentSpeech(string componentName, double value, Ohlcv bar,
            IReadOnlyDictionary<string, double[]> allComponentData, int dataIndex)
        {
            // Return null for the signal dot — DefaultSignalSpeechTemplate handles it.
            // For the line, provide richer zone context than the template alone can offer.
            if (componentName != CompLine) return null;
            if (double.IsNaN(value))       return "no data";

            // We could also read other components here if needed:
            // double other = allComponentData.TryGetValue("Some Other", out var arr)
            //     && dataIndex < arr.Length ? arr[dataIndex] : double.NaN;

            if (value > 70)  return $"{value:F1}, overbought";
            if (value < -70) return $"{value:F1}, oversold";
            return $"{value:F1}";

            // Returning null here would fall through to SpeechTemplate:
            //   "Momentum. Oscillator. {value:F1}."
        }

        public List<LevelDescriptor> GetDefaultLevels(string code)
        {
            if (!code.Equals("SIMPLE_MOMENTUM", StringComparison.OrdinalIgnoreCase))
                return new();

            return new()
            {
                // Overbought threshold — red dashed, matches the signal dot trigger level.
                new("Overbought",  70.0, "#EF5350", DashStyle.Dash, PlayEarcon: true),
                // Zero line — gray dashed, standard zero-crossing reference.
                new("Zero",         0.0, "#757575", DashStyle.Dash),
                // Oversold threshold — green dashed.
                new("Oversold",   -70.0, "#66BB6A", DashStyle.Dash, PlayEarcon: true),
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void WriteSpan(IIndicatorResultBuffer buffer,
            string name, double[] data, int n)
        {
            var span = buffer.GetComponentSpan(name);
            int len = Math.Min(span.Length, n);
            for (int i = 0; i < len; i++) span[i] = data[i];
        }

        private static int GetInt(Dictionary<string, object> p, string key, int def)
            => p.TryGetValue(key, out var v) && v != null
               ? (int)(double)v : def;

        private static double GetDbl(Dictionary<string, object> p, string key, double def)
            => p.TryGetValue(key, out var v) && v != null
               ? (double)v : def;

        private static double GetVal(IReadOnlyDictionary<string, double[]> r,
            string key, int idx)
        {
            if (!r.TryGetValue(key, out var arr) || arr == null || idx >= arr.Length)
                return double.NaN;
            return arr[idx];
        }
    }
}
```

### Deployment checklist for an external plugin DLL

1. Name the project output `AccessibleTrader.Plugins.<YourName>.dll`.
2. Reference `AccessibleTrader.Sdk` but do **not** copy it to the output — the host
   already has it. Set `<Private>false</Private>` on the SDK reference.
3. Include all third-party math or data dependencies in the plugin's output folder
   alongside the main DLL.
4. Drop the DLL (and any dependencies) into one of the three scanned directories:
   - The app base directory (same folder as `AccessibleTrader.dll`)
   - `<app-base>/Plugins/`
   - `%LOCALAPPDATA%\AccessibleTrader\Plugins\` (user drop-in)
5. Restart the application. The provider will be discovered, instantiated, and added
   to the indicator catalogue automatically.
