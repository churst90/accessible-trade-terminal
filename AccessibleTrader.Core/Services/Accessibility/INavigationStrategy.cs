using AccessibleTrader.Core.Models;
using AccessibleTrader.Sdk.Models;

namespace AccessibleTrader.Core.Services.Accessibility
{
    /// <summary>
    /// Defines the contract for navigating through a specific type of chart series.
    /// Strategies encapsulate the logic for X/Y movement.
    /// </summary>
    public interface INavigationStrategy
    {
        /// <summary>
        /// Calculates the result of an X-axis (horizontal/time) move.
        /// </summary>
        NavigationResult NavigateX(WorkspaceState state, int delta);

        /// <summary>
        /// Calculates the result of a Y-axis (vertical/component/bin) move.
        /// </summary>
        NavigationResult NavigateY(WorkspaceState state, int delta);
    }

    /// <summary>
    /// Represents the result of a navigation operation.
    /// </summary>
    /// <param name="Success">True if the move was valid and state changed.</param>
    /// <param name="NewIndex">The new absolute data index (X-axis).</param>
    /// <param name="NewComponentIndex">The new component index (Y-axis).</param>
    /// <param name="NewBinIndex">The new price bin index (Y-axis for distributions).</param>
    /// <param name="Context">The updated interaction context.</param>
    /// <param name="FeedbackType">The category of feedback to provide.</param>
    /// <param name="FeedbackMessage">Specific message or earcon ID.</param>
    public record NavigationResult(
        bool Success,
        int NewIndex = -1,
        int NewComponentIndex = -1,
        int NewBinIndex = -1,
        InteractionContext Context = InteractionContext.Series,
        FeedbackType FeedbackType = FeedbackType.Navigation,
        string? FeedbackMessage = null
    );

    /// <summary>
    /// Logical navigation directions.
    /// </summary>
    public enum NavigationDirection
    {
        Left,
        Right,
        Up,
        Down
    }
}
