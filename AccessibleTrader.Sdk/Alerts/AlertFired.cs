namespace AccessibleTrader.Sdk.Alerts;

public record AlertFired(
    AlertDefinition Definition,
    double TriggeringValue,
    double? PreviousValue,
    string SpeechText,
    // The display-name of the symbol the alert fired against (the on-screen
    // chart at fire time). Carried on the fired event so delivery payloads and
    // per-asset webhook routing know which market this alert belongs to, even
    // when the definition's Symbol scope was left null ("any / current chart").
    // Additive with a null default so existing constructions still compile.
    string? Symbol = null
);
