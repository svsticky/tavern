import { beforeEach, describe, expect, it, vi } from "vitest";

const { getStatistics } = vi.hoisted(() => ({ getStatistics: vi.fn() }));
vi.mock("~/api", () => ({ getStatistics }));

import {
  distributionToChartData,
  formatCommitteeYear,
  getFirstDataYear,
  getSelectedActivityYear,
  getSelectedYear,
  loadStatistics,
  MAX_DISTRIBUTION_COLUMNS,
  mergeStudyRows,
  studyCountsToChartData,
  yearCountsToChartData,
} from "~/routes/admin/statistics/statistics.handlers";

const params = (search = "") => new URLSearchParams(search);

describe("getSelectedYear", () => {
  it("uses a valid year from the URL", () => {
    expect(getSelectedYear(params("?year=2015"), 2026)).toBe(2015);
  });

  it.each([
    "",
    "?year=",
    "?year=abc",
    "?year=0",
    "?year=-3",
    "?year=2027",
  ])("falls back to the current year for %j", (search) => {
    expect(getSelectedYear(params(search), 2026)).toBe(2026);
  });
});

describe("getFirstDataYear", () => {
  it("returns the oldest year in any series", () => {
    expect(
      getFirstDataYear(
        {
          year: 2026,
          snapshotAt: "",
          newMembersPerYear: [{ year: 2010, count: 1 }],
          activeMembersPerYear: [{ year: 2012, count: 1 }],
          activeMembersPerStudy: [{ year: 2009, studies: [] }],
          totalMembersPerYear: [{ year: 2008, count: 1 }],
        },
        2026,
      ),
    ).toBe(2008);
  });

  it("falls back when there is no data", () => {
    expect(getFirstDataYear({ year: 2026, snapshotAt: "" }, 2026)).toBe(2026);
  });
});

describe("loadStatistics", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads the current year without a year parameter", async () => {
    const statistics = { year: 2026 };
    getStatistics.mockResolvedValue({ data: statistics });

    await expect(loadStatistics(2026, 2026)).resolves.toBe(statistics);
    expect(getStatistics).toHaveBeenCalledWith(undefined);
  });

  it("requests an older year", async () => {
    const statistics = { year: 2019 };
    getStatistics.mockResolvedValue({ data: statistics });

    await expect(loadStatistics(2019, 2026)).resolves.toBe(statistics);
    expect(getStatistics).toHaveBeenCalledWith({ query: { year: 2019 } });
  });

  it("throws the API error when the request fails", async () => {
    const error = { message: "Forbidden" };
    getStatistics.mockResolvedValue({ error });

    await expect(loadStatistics(2026, 2026)).rejects.toBe(error);
  });

  it("throws a generic error when no data is returned", async () => {
    getStatistics.mockResolvedValue({});

    await expect(loadStatistics(2026, 2026)).rejects.toThrow(
      "Failed to load statistics",
    );
  });
});

describe("chart helpers", () => {
  it("formatCommitteeYear formats long and short", () => {
    expect(formatCommitteeYear(2025)).toBe("2024-2025");
    expect(formatCommitteeYear(2025, true)).toBe("24-25");
  });

  it("yearCountsToChartData fills missing years with zero", () => {
    const result = yearCountsToChartData([
      { year: 2022, count: 4 },
      { year: 2024, count: 7 },
    ]);

    expect(result.map((d) => [d.key, d.value])).toEqual([
      ["2022", 4],
      ["2023", 0],
      ["2024", 7],
    ]);
    expect(result[0].fullLabel).toBe("2021-2022");
  });

  it("yearCountsToChartData returns empty for no data", () => {
    expect(yearCountsToChartData(null)).toEqual([]);
    expect(yearCountsToChartData([])).toEqual([]);
  });

  it("studyCountsToChartData labels entries without a study", () => {
    const result = studyCountsToChartData([
      { studyId: 1, studyTitle: "CS", count: 5 },
      { studyId: null, studyTitle: null, count: 2 },
    ]);

    expect(result).toEqual([
      { key: "1", label: "CS", fullLabel: "CS", value: 5 },
      { key: "none", label: "no_study", fullLabel: "no_study", value: 2 },
    ]);
    expect(studyCountsToChartData(undefined)).toEqual([]);
  });
});

describe("mergeStudyRows", () => {
  it("combines enrollments, new members, active members and new active members per study", () => {
    const rows = mergeStudyRows({
      enrollments: [
        { studyId: 1, studyTitle: "CS", count: 5 },
        { studyId: 2, studyTitle: "Math", count: 0 },
      ],
      newMembers: [
        { studyId: 1, studyTitle: "CS", count: 1 },
        { studyId: 3, studyTitle: "AI", count: 4 },
      ],
      activeMembers: [
        { studyId: 1, studyTitle: "CS", count: 2 },
        { studyId: null, studyTitle: null, count: 3 },
      ],
      newActiveMembers: [{ studyId: 3, studyTitle: "AI", count: 2 }],
    });

    expect(rows).toEqual([
      {
        key: "1",
        title: "CS",
        enrolled: 5,
        newMembers: 1,
        activeMembers: 2,
        newActiveMembers: 0,
      },
      {
        key: "3",
        title: "AI",
        enrolled: 0,
        newMembers: 4,
        activeMembers: 0,
        newActiveMembers: 2,
      },
      {
        key: "none",
        title: "no_study",
        enrolled: 0,
        newMembers: 0,
        activeMembers: 3,
        newActiveMembers: 0,
      },
    ]);
    expect(
      mergeStudyRows({
        enrollments: undefined,
        newMembers: null,
        activeMembers: null,
      }),
    ).toEqual([]);
  });
});

describe("distributionToChartData", () => {
  it("has a column for every number of activities up to the highest, filling gaps with 0", () => {
    const result = distributionToChartData([
      { activities: 1, members: 5 },
      { activities: 3, members: 2 },
    ]);

    expect(result.map((d) => [d.key, d.value])).toEqual([
      ["1", 5],
      ["2", 0],
      ["3", 2],
    ]);
    expect(result[0].fullLabel).toBe("statistics_activities_count");
  });

  it("groups into ranges when there would be too many columns", () => {
    const highest = MAX_DISTRIBUTION_COLUMNS * 3; // 90 -> ranges of 3
    const result = distributionToChartData([
      { activities: 1, members: 4 },
      { activities: 2, members: 1 },
      { activities: 4, members: 2 },
      { activities: highest, members: 1 },
    ]);

    expect(result).toHaveLength(MAX_DISTRIBUTION_COLUMNS);
    expect(result[0]).toMatchObject({ key: "1-3", value: 5 });
    expect(result[1]).toMatchObject({ key: "4-6", value: 2 });
    expect(result.at(-1)).toMatchObject({ key: "88-90", value: 1 });
    expect(result[0].fullLabel).toBe("statistics_activities_range");
  });

  it("returns empty for no data", () => {
    expect(distributionToChartData(undefined)).toEqual([]);
    expect(distributionToChartData([])).toEqual([]);
  });
});

describe("getSelectedActivityYear", () => {
  const params = (search = "") => new URLSearchParams(search);

  it("uses the year from the URL when it has data", () => {
    expect(
      getSelectedActivityYear(params("activityYear=2024"), [2026, 2024], 2026),
    ).toBe(2024);
  });

  it("falls back to the current year, then to the most recent year with data", () => {
    expect(
      getSelectedActivityYear(params("activityYear=1999"), [2026, 2024], 2026),
    ).toBe(2026);
    expect(getSelectedActivityYear(params(), [2024, 2025], 2026)).toBe(2025);
    expect(getSelectedActivityYear(params(), [], 2026)).toBeNull();
  });
});
