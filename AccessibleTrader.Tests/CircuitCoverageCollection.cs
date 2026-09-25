namespace AccessibleTrader.Tests
{
    // Serialises every test class that reads or writes the WebHost's process-global circuit
    // registries: CircuitAlertCoverage (which symbols an open browser already watches) and
    // BrowserPresence (whether a browser is connected at all). The headless monitor's poll
    // reads CircuitAlertCoverage on every pass, so a class that polls it through
    // HeadlessMonitorHarness is a READER even though it never registers anything.
    //
    // ProfileNarrationTests was that reader, outside the collection, for two weeks. It polls
    // BTC/USD; HeadlessNarrationTests registers a pretend circuit covering BTC/USD. When the two
    // overlapped under load, the poll saw BTC/USD as the browser's, the point-of-control cross
    // was routed to a browser that did not exist, and the test heard nothing. Demonstrated on
    // 2026-09-25 by registering that circuit around the test. It was one of the flakes the
    // mutation campaigns kept counting as catches.
    //
    // CircuitCoverageEnrollmentTests keeps membership honest in both directions.
    [CollectionDefinition("CircuitCoverage")]
    public sealed class CircuitCoverageCollection { }
}
