import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import StatisticsChart, {
  niceTicks,
} from "~/components/Statistics/StatisticsChart";

describe("niceTicks", () => {
  it("rounds the maximum up to a nice value", () => {
    expect(niceTicks(80)).toEqual([0, 20, 40, 60, 80]);
    expect(niceTicks(417)).toEqual([0, 100, 200, 300, 400, 500]);
    // counts: whole-number steps only
    expect(niceTicks(1)).toEqual([0, 1]);
    expect(niceTicks(2)).toEqual([0, 1, 2]);
    expect(niceTicks(7)).toEqual([0, 2, 4, 6, 8]);
    expect(niceTicks(0)).toEqual([0, 1]);
  });
});

const data = [
  { key: "2025", label: "24-25", fullLabel: "2024-2025", value: 10 },
  { key: "2026", label: "25-26", fullLabel: "2025-2026", value: 30 },
];

describe("StatisticsChart", () => {
  it("shows the empty state", () => {
    render(<StatisticsChart data={[]} label="x" />);
    expect(screen.getByText("no_data")).toBeInTheDocument();
  });

  it("labels the most recent year and has an accessible table without a visible caption", () => {
    const { container } = render(
      <StatisticsChart data={data} label="New members" />,
    );

    expect(container.querySelector("caption")).toBeNull();
    expect(screen.getByRole("table")).toHaveTextContent("2024-202510");
    expect(screen.getByTitle("2025-2026: 30")).toHaveTextContent("30");
    expect(screen.getByTitle("2024-2025: 10")).not.toHaveTextContent("10");
    expect(container.querySelector("polyline")).toBeNull();
  });

  it("draws a line through the years for the line variant", () => {
    const { container } = render(
      <StatisticsChart data={data} label="Members" variant="line" />,
    );

    // x: padding 4 + half a year (16), then one year + gap (36) further; y: percent from the top
    // of the axis maximum (30, so 10 is 66.67 from the top and 30 is at the top).
    expect(container.querySelector("polyline")?.getAttribute("points")).toBe(
      "20,66.67 56,0",
    );
    expect(screen.getByTitle("2025-2026: 30")).toHaveTextContent("30");
  });

  it("starts scrolled to the most recent year", () => {
    const scrollWidth = vi
      .spyOn(HTMLElement.prototype, "scrollWidth", "get")
      .mockReturnValue(900);

    const { container } = render(<StatisticsChart data={data} label="x" />);

    const scroller = container.querySelector(".overflow-x-auto") as HTMLElement;
    expect(scroller.scrollLeft).toBe(900);
    scrollWidth.mockRestore();
  });

  it("labels the largest value and starts at the left for a distribution", () => {
    const scrollWidth = vi
      .spyOn(HTMLElement.prototype, "scrollWidth", "get")
      .mockReturnValue(900);
    const distribution = [
      { key: "1", label: "1", fullLabel: "1 activity", value: 4 },
      { key: "2", label: "2", fullLabel: "2 activities", value: 9 },
      { key: "3", label: "3", fullLabel: "3 activities", value: 2 },
    ];

    const { container } = render(
      <StatisticsChart
        data={distribution}
        label="Members"
        categoryLabel="Activities"
        emphasis="max"
      />,
    );

    expect(screen.getByTitle("2 activities: 9")).toHaveTextContent("9");
    expect(screen.getByTitle("3 activities: 2")).not.toHaveTextContent("2");
    expect(screen.getByRole("table")).toHaveTextContent("ActivitiesMembers");
    const scroller = container.querySelector(".overflow-x-auto") as HTMLElement;
    expect(scroller.scrollLeft).toBe(0);
    scrollWidth.mockRestore();
  });
});
