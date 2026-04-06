namespace AccessibleTrader.Sdk.Interfaces;

/// <summary>
/// Monitors narrated series for new indicator signals and zone transitions,
/// announcing them via TTS as they occur on live bar closes.
/// Self-wires via EventBus subscriptions in the constructor — no public trigger methods.
/// </summary>
public interface IAutoNarrationService
{
}
