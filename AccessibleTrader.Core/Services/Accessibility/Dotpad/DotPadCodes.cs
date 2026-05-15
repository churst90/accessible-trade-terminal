namespace AccessibleTrader.Core.Services.Accessibility.Dotpad
{
    /// <summary>
    /// Mirrors DOT_DATA_CODE in dot_pad_sdk.h. These codes are passed to the message
    /// callback when the device reports lifecycle / metadata events.
    /// </summary>
    public enum DotPadDataCode
    {
        Connected = 0,
        Disconnected = 1,
        BoardInfo = 2,
        BleMacAddress = 3,
        DeviceName = 4,
        DeviceFwVersion = 5,
        DeviceHwVersion = 6,
        ResponseDisplayLineAck = 7,
        ResponseDisplayLineNonAck = 8,
        ResponseDisplayLineComplete = 9,
        CommandError = 10,
        CommandNone = 11,
    }

    /// <summary>
    /// Mirrors DOT_KEY_CODE in dot_pad_sdk.h. Reported via the key callback when a
    /// hardware button on the Dot Pad is pressed.
    /// </summary>
    public enum DotPadKeyCode
    {
        Function1 = 0,
        Function2 = 1,
        Function3 = 2,
        Function4 = 3,
        Function12 = 4,
        Function13 = 5,
        Function14 = 6,
        Function23 = 7,
        Function24 = 8,
        Function34 = 9,
        Else = 10,
        PanningAll = 11,
        PanningLeft = 12,
        PanningRight = 13,
        Lpf1 = 14,
        Rpf4 = 15,
    }

    /// <summary>
    /// Language constants accepted by DOT_PAD_SET_LANGUAGE and DOT_PAD_BRAILLE_DISPLAY.
    /// Values per the SDK sample app — confirm against the Windows SDK Guide PDF if
    /// adding new languages.
    /// </summary>
    public enum DotPadLanguage
    {
        English = 0,
        Korean = 1,
        Japanese = 2,
    }

    /// <summary>
    /// Braille grade. 1 = uncontracted; 2 = contracted (UEB-2 for English).
    /// </summary>
    public enum DotPadBrailleGrade
    {
        Grade1 = 1,
        Grade2 = 2,
    }
}
