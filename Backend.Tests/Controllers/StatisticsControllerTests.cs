using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Controllers.DTOs;
using Backend.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Backend.Tests.Controllers;

public class StatisticsControllerTests
{
    private readonly IStatisticsService _serviceMock;
    private readonly StatisticsController _controller;
    private readonly Guid _userId = Guid.NewGuid();

    public StatisticsControllerTests()
    {
        _serviceMock = Substitute.For<IStatisticsService>();
        _controller = new StatisticsController(_serviceMock)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("UserId", _userId.ToString())], "mock"))
                }
            }
        };
    }

    [Fact]
    public async Task GetStatistics_RequestsAllStatistics()
    {
        var response = new StatisticsResponseDTO { Year = 2026, SnapshotAt = DateTimeOffset.UtcNow, TotalMembersPerYear = [] };
        _serviceMock.GetStatistics(Arg.Any<IReadOnlySet<StatisticType>>(), Arg.Any<uint?>(), _userId, Arg.Any<CancellationToken>()).Returns(response);

        var result = await _controller.GetStatistics(2024, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(response, ok.Value);
        await _serviceMock.Received(1).GetStatistics(
            Arg.Is<IReadOnlySet<StatisticType>>(s => s.SetEquals(Enum.GetValues<StatisticType>())),
            2024u,
            _userId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStatistic_RequestsOnlyThatStatistic()
    {
        var response = new StatisticsResponseDTO { Year = 2026, SnapshotAt = DateTimeOffset.UtcNow, ActiveMembersPerYear = [] };
        _serviceMock.GetStatistics(Arg.Any<IReadOnlySet<StatisticType>>(), Arg.Any<uint?>(), _userId, Arg.Any<CancellationToken>()).Returns(response);

        var result = await _controller.GetStatistic(StatisticType.ActiveMembersPerYear, null, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(response, ok.Value);
        await _serviceMock.Received(1).GetStatistics(
            Arg.Is<IReadOnlySet<StatisticType>>(s => s.Count == 1 && s.Contains(StatisticType.ActiveMembersPerYear)),
            (uint?)null,
            _userId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetStatistics_Unauthorized_Propagates()
    {
        _serviceMock.GetStatistics(Arg.Any<IReadOnlySet<StatisticType>>(), Arg.Any<uint?>(), _userId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new UnauthorizedAccessException());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _controller.GetStatistics(null, CancellationToken.None));
    }
}
