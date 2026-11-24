using Backend.Controllers.DTOs;
using Backend.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Controller exposing association-wide statistics for the board, such as member counts, study distributions, new and active members per committee year, and first-year activity attendance. All statistics can be loaded at once, or a single statistic can be requested to avoid computing data that is not needed. Every "per year" statistic is grouped by committee year (starting on the CommitteeCreationDate setting), and deleted member accounts are included so historical numbers remain stable. Access is restricted to board and candidate board members.
/// </summary>
/// <param name="statisticsService">The statistics service responsible for computing the statistics.</param>
[Route("[controller]")]
[ApiController]
[Authorize]
public class StatisticsController(IStatisticsService statisticsService) : ControllerBase
{
    /// <summary>
    /// Helper method to extract the unique identifier of the currently authenticated user from the request claims.
    /// </summary>
    /// <returns>A Guid representing the authenticated user's ID.</returns>
    private Guid GetUserId()
    {
        return Guid.Parse(User.Claims.First(c => c.Type == "UserId").Value);
    }

    // GET: statistics
    /// <summary>
    /// Retrieves all available statistics in a single response. Intended for the administrative statistics page, which shows every statistic at once. Only accessible to board and candidate board members.
    /// </summary>
    /// <param name="year">The committee year for the point-in-time statistics; defaults to the current year.</param>
    /// <param name="ct">The cancellation token to monitor for request cancellation.</param>
    /// <returns>All statistics.</returns>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(StatisticsResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StatisticsResponseDTO>> GetStatistics([FromQuery] uint? year, CancellationToken ct)
    {
        var all = Enum.GetValues<StatisticType>().ToHashSet();
        return Ok(await statisticsService.GetStatistics(all, year, GetUserId(), ct));
    }

    // GET: statistics/{statistic}
    /// <summary>
    /// Retrieves a single statistic. Only the property of the response that corresponds to the requested statistic is populated; all others are null. Only accessible to board and candidate board members.
    /// </summary>
    /// <param name="statistic">The statistic to retrieve.</param>
    /// <param name="year">The committee year for the point-in-time statistics; defaults to the current year.</param>
    /// <param name="ct">The cancellation token to monitor for request cancellation.</param>
    /// <returns>The statistics response with only the requested statistic populated.</returns>
    [HttpGet("{statistic}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(StatisticsResponseDTO), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<StatisticsResponseDTO>> GetStatistic(StatisticType statistic, [FromQuery] uint? year, CancellationToken ct)
    {
        return Ok(await statisticsService.GetStatistics(new HashSet<StatisticType> { statistic }, year, GetUserId(), ct));
    }
}
