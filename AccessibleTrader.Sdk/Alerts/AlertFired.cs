namespace AccessibleTrader.Sdk.Alerts;

public record AlertFired(
    AlertDefinition Definition,
    double TriggeringValue,
    double? PreviousValue,
    string SpeechText
);
