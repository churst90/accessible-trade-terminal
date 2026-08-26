namespace AccessibleTrader.Tests
{
    // Defines the "ScriptWorker" collection so xUnit SERIALIZES every class that spawns a real
    // ScriptWorker process, shells out to bwrap, or runs a Roslyn compilation on the way there.
    //
    // The A2 sabotage audit (2026-08-26) ran the full suite 28 times and logged 7 spurious
    // failures from 4 tests; the two worst offenders were in here
    // (LinuxBwrapSandboxTests.A_script_cannot_read_the_hosts_environment and
    // StrategyCausalityGateTests.CompileStrategyAsync_loads_a_causal_script), both green in
    // isolation. Cause: the classes below all contend for process spawn, bwrap setup and the
    // worker's working-set budget under full parallel load — ScriptWorkerMemoryLimitTests in
    // particular asserts against WorkingSet64, which is a *machine* resource, not a test one.
    //
    // Five of those 7 spurious failures were the ONLY failure in their run, which is how a flake
    // stops being an annoyance and starts inverting verdicts: it hands back a red tick for a
    // mutant that the suite did not actually catch.
    //
    // Same shape as ProviderCredentialBridgeCollection — see that file for the first instance.
    [CollectionDefinition("ScriptWorker")]
    public sealed class ScriptWorkerCollection { }
}
