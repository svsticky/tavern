using Backend.Controllers.DTOs;

namespace Backend.Interfaces;

/// <summary>
/// Defines the contract for computing association-wide statistics for administrators.
/// </summary>
public interface IStatisticsService
{
    /// <summary>
    /// Computes the requested statistics. Only the properties of <see cref="StatisticsResponseDTO"/> that correspond to
    /// the requested <paramref name="statistics"/> are populated; all others are left null. Only the data needed for the
    /// requested statistics is loaded. Deleted (anonymized) members are included in all statistics.
    /// </summary>
    /// <param name="statistics">The statistics to compute.</param>
    /// <param name="year">
    /// The committee year for the point-in-time statistic (active enrollments per study): the state at the end of that
    /// year, or now for the current year. Null means the current year.
    /// </param>
    /// <param name="userId">The ID of the requesting user, who must be a board or candidate board member.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The computed statistics.</returns>
    /// <exception cref="ArgumentException">When <paramref name="year"/> lies in the future.</exception>
    Task<StatisticsResponseDTO> GetStatistics(IReadOnlySet<StatisticType> statistics, uint? year, Guid userId, CancellationToken ct);
}
