namespace AccessibleTrader.Sdk.Interfaces;

public interface IPaneAssignmentService
{
    string GetPane(string indicatorCode);
    string GetCategory(string indicatorCode);
}
