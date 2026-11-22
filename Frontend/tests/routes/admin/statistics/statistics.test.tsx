import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { StatisticsResponseDto } from "~/api";
import { renderWithProviders } from "~/testUtils";

const { loadStatistics, toastError } = vi.hoisted(() => ({
  loadStatistics: vi.fn(),
  toastError: vi.fn(),
}));

vi.mock(
  "~/routes/admin/statistics/statistics.handlers",
  async (importOriginal) => ({
    ...(await importOriginal<
      typeof import("~/routes/admin/statistics/statistics.handlers")
    >()),
    loadStatistics,
  }),
);

vi.mock("~/util/date.util", async (importOriginal) => ({
  ...(await importOriginal<typeof import("~/util/date.util")>()),
  getCommitteeYear: () => 2026,
}));

vi.mock("react-hot-toast", () => ({ default: { error: toastError } }));

import Statistics from "~/routes/admin/statistics/statistics";

/** Renders the statistics page, optionally with a search string such as `?year=2025`. */
function renderStatistics(search = "") {
  return renderWithProviders(<Statistics />, {
    route: `/admin/statistics${search}`,
  });
}

const statistics: StatisticsResponseDto = {
  year: 2026,
  snapshotAt: "2026-11-16T12:00:00Z",
  totalMembersPerYear: [
    { year: 2025, count: 300 },
    { year: 2026, count: 321 },
  ],
  activeEnrollmentsPerStudy: [
    { studyId: 1, studyTitle: "Computer Science", count: 200 },
  ],
  newMembersPerYear: [
    { year: 2025, count: 90 },
    { year: 2026, count: 101 },
  ],
  newBachelorMembersPerYear: [{ year: 2026, count: 77 }],
  newMasterMembersPerYear: [{ year: 2026, count: 24 }],
  activeMembersPerYear: [{ year: 2026, count: 55 }],
  activeMembersPerStudy: [
    { year: 2025, studies: [{ studyId: 1, studyTitle: "Old CS", count: 9 }] },
    { year: 2026, studies: [{ studyId: null, studyTitle: null, count: 4 }] },
  ],
  firstYearsAtActivitiesPerYear: [{ year: 2026, count: 66 }],
  activitiesPerInvolvedMember: [
    {
      year: 2025,
      distribution: [{ activities: 2, members: 17 }],
    },
    {
      year: 2026,
      distribution: [
        { activities: 1, members: 50 },
        { activities: 4, members: 7 },
      ],
    },
  ],
  newActiveMembersPerStudy: [
    {
      year: 2026,
      studies: [{ studyId: 1, studyTitle: "Computer Science", count: 13 }],
    },
  ],
  newMembersPerStudy: [
    {
      year: 2026,
      studies: [{ studyId: 1, studyTitle: "Computer Science", count: 88 }],
    },
  ],
};

/** The series select of the per-year chart, the year select of the activity distribution and of the per-study table. */
const selects = () => {
  const [series, activityYear, year] = screen.getAllByRole(
    "combobox",
  ) as HTMLSelectElement[];
  return { series, activityYear, year };
};

describe("Statistics (admin)", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    loadStatistics.mockImplementation(async (year: number) =>
      year === 2026 ? statistics : { ...statistics, year },
    );
  });

  it("shows the loading message until the statistics are loaded", async () => {
    let finish: (value: StatisticsResponseDto) => void = () => {};
    loadStatistics.mockReturnValue(
      new Promise((resolve) => {
        finish = resolve;
      }),
    );

    renderStatistics();

    expect(screen.queryByText("loading")).toBeInTheDocument();
    finish(statistics);
    expect(await screen.findByText("statistics")).toBeInTheDocument();
  });

  it("shows an error when the statistics fail to load", async () => {
    loadStatistics.mockRejectedValue(new Error("Failed to load statistics"));
    vi.spyOn(console, "error").mockImplementation(() => {});

    renderStatistics();

    expect(await screen.findByText("loading_failed")).toBeInTheDocument();
    expect(toastError).toHaveBeenCalled();
  });

  it("keeps the back button and shows the number of members as a line by default", async () => {
    const { container } = renderStatistics();

    expect(await screen.findByText("statistics")).toBeInTheDocument();
    expect(screen.getByText("back")).toBeInTheDocument();
    expect(selects().series.value).toBe("total_members");
    expect(container.querySelector("polyline")).not.toBeNull();
    expect(screen.getByTitle("2025-2026: 321")).toHaveTextContent("321");
  });

  it("switches the chart between series", async () => {
    const { container } = renderStatistics();
    await screen.findByText("statistics");

    fireEvent.change(selects().series, {
      target: { value: "new_master_members" },
    });
    await waitFor(() =>
      expect(screen.getByTitle("2025-2026: 24")).toHaveTextContent("24"),
    );
    expect(container.querySelector("polyline")).toBeNull();
  });

  it("shows the per-study table for the current year, with the year select in that section", async () => {
    renderStatistics();
    await screen.findByText("statistics");

    const { year } = selects();
    expect(year.value).toBe("2026");
    expect(year.options[0].textContent).toBe("2025-2026");
    expect(year.options[year.options.length - 1].textContent).toBe("2024-2025");
    expect(
      screen.getByText("statistics_per_study_subtitle_now"),
    ).toBeInTheDocument();
    expect(screen.getByText("Computer Science")).toBeInTheDocument();
    expect(screen.getByText("88")).toBeInTheDocument(); // new members
    expect(screen.getByText("new_active_members")).toBeInTheDocument();
    expect(screen.getByText("13")).toBeInTheDocument(); // new members that were also active
    expect(screen.getAllByText("no_study").length).toBeGreaterThan(0); // active members without study
  });

  it("loads an older year for the per-study table when it is selected", async () => {
    renderStatistics();
    await screen.findByText("statistics");

    fireEvent.change(selects().year, { target: { value: "2025" } });

    expect(
      await screen.findByText("statistics_per_study_subtitle_end_of_year"),
    ).toBeInTheDocument();
    expect(loadStatistics).toHaveBeenLastCalledWith(2025, 2026);
    // active members per study follow the selected year (9 for study 1 in 2025)
    expect(screen.getAllByText("9").length).toBeGreaterThan(0);
  });

  it("opens an older year from the URL", async () => {
    renderStatistics("?year=2025");

    expect(
      await screen.findByText("statistics_per_study_subtitle_end_of_year"),
    ).toBeInTheDocument();
    expect(selects().year.value).toBe("2025");
  });

  it("shows the activity distribution of the current year and switches year without reloading", async () => {
    renderStatistics();
    await screen.findByText("statistics");

    expect(selects().activityYear.value).toBe("2026");
    expect(
      screen.getByTitle("statistics_activities_count: 50"),
    ).toHaveTextContent("50");
    expect(screen.getAllByTitle("statistics_activities_count: 0")).toHaveLength(
      2,
    ); // 2 and 3 activities: gaps filled with 0
    const loads = loadStatistics.mock.calls.length;

    fireEvent.change(selects().activityYear, { target: { value: "2025" } });

    await waitFor(() =>
      expect(
        screen.getByTitle("statistics_activities_count: 17"),
      ).toHaveTextContent("17"),
    );
    expect(loadStatistics.mock.calls.length).toBe(loads);
  });
});
