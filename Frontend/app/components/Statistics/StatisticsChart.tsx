import { t } from "i18next";
import { useEffect, useRef } from "react";
import type { ChartDatum } from "~/routes/admin/statistics/statistics.handlers";

/** Width of one column on the x-axis, and the gap between columns, in pixels (matches `w-8` and `gap-1`). */
const YEAR_WIDTH = 32;
const YEAR_GAP = 4;
/** Horizontal padding of the plot area in pixels (matches `px-1`). */
const PLOT_PADDING = 4;

/**
 * Props for the StatisticsChart component.
 * @property {ChartDatum[]} data - The values in x-axis order (e.g. one per committee year, oldest first).
 * @property {string} label - Accessible name of the chart, also the value column of the screen-reader table.
 * @property {"column" | "line"} [variant] - Columns (default), or a line for a level over time.
 * @property {string} [categoryLabel] - Name of the x-axis for the screen-reader table; defaults to "year".
 * @property {"last" | "max"} [emphasis] - Which value is labelled and scrolled into view: the last one (default,
 *   the most recent year) or the largest one (e.g. the peak of a distribution, starting scrolled to the left).
 */
interface StatisticsChartProps {
  data: ChartDatum[];
  label: string;
  variant?: "column" | "line";
  categoryLabel?: string;
  emphasis?: "last" | "max";
}

/**
 * Rounds the largest value up to a "nice" axis maximum and returns the gridline values from 0 up
 * to that maximum. The values are counts, so steps are whole numbers (1, 2 or 5 times a power of
 * ten, at least 1) and there are at most five steps.
 */
export const niceTicks = (max: number): number[] => {
  if (max <= 0) return [0, 1];
  const step = 10 ** Math.max(0, Math.floor(Math.log10(max / 5)));
  const niceStep =
    [1, 2, 5, 10].map((m) => m * step).find((s) => max / s <= 5) ?? step * 10;
  const top = Math.ceil(max / niceStep) * niceStep;
  return Array.from(
    { length: Math.round(top / niceStep) + 1 },
    (_, i) => i * niceStep,
  );
};

/**
 * A single-series chart for the statistics page, drawn as columns or as a line: values per committee
 * year, or a distribution such as the number of members per number of attended activities.
 *
 * Every column has a fixed width, so a few values don't spread over the full width, and the chart
 * scrolls horizontally when there are many (e.g. on a phone). One value is labelled (the last or the
 * largest, see `emphasis`); every value shows in a tooltip on hover. The same values are available
 * to screen readers in a visually hidden table.
 *
 * @component
 */
export default function StatisticsChart({
  data,
  label,
  variant = "column",
  categoryLabel,
  emphasis = "last",
}: StatisticsChartProps) {
  const scrollRef = useRef<HTMLDivElement>(null);
  const dataKeys = data.map((d) => d.key).join(",");

  // Show the most recent year (or, for a distribution, the start) when the data changes. Only
  // depends on the keys, so a re-render with the same values doesn't undo the viewer's own scrolling.
  // biome-ignore lint/correctness/useExhaustiveDependencies: dataKeys stands in for data
  useEffect(() => {
    const container = scrollRef.current;
    if (container)
      container.scrollLeft = emphasis === "last" ? container.scrollWidth : 0;
  }, [dataKeys, variant, emphasis]);

  if (data.length === 0) {
    return <span className="text-sm text-slate-400">{t("no_data")}</span>;
  }

  const ticks = niceTicks(Math.max(...data.map((d) => d.value)));
  const top = ticks[ticks.length - 1];
  const lastIndex = data.length - 1;
  const max = Math.max(...data.map((d) => d.value));
  const labelledIndex =
    emphasis === "max" ? data.findIndex((d) => d.value === max) : lastIndex;
  const percent = (value: number) => (value / top) * 100;

  // The line is drawn in pixel units horizontally (so its points line up with the fixed-width
  // years) and in percent of the plot height vertically.
  const plotWidth =
    2 * PLOT_PADDING + data.length * YEAR_WIDTH + lastIndex * YEAR_GAP;
  const linePoints = data
    .map(
      (d, i) =>
        `${PLOT_PADDING + i * (YEAR_WIDTH + YEAR_GAP) + YEAR_WIDTH / 2},${Math.round((100 - percent(d.value)) * 100) / 100}`,
    )
    .join(" ");

  return (
    <figure className="w-full m-0" aria-label={label}>
      {/* pt-6 on both the axis and the scroll area: room for the top tick label and the value label
          above the highest value, inside the chart (the scroll area would otherwise clip them and
          they would overlap whatever is above the chart). */}
      <div className="flex gap-2 pt-6" aria-hidden="true">
        <div className="relative h-48 w-8 shrink-0 text-xs text-slate-400 tabular-nums">
          {ticks.map((tick) => (
            <span
              key={tick}
              className="absolute right-0 translate-y-1/2"
              style={{ bottom: `${percent(tick)}%` }}
            >
              {tick}
            </span>
          ))}
        </div>
        <div
          ref={scrollRef}
          className="flex-1 min-w-0 overflow-x-auto -mt-6 pt-6 pb-1"
        >
          <div className="relative h-48 w-max min-w-full">
            {ticks.map((tick) => (
              <div
                key={tick}
                className={`absolute inset-x-0 border-t ${tick === 0 ? "border-slate-300" : "border-slate-100"}`}
                style={{ bottom: `${percent(tick)}%` }}
              />
            ))}
            {variant === "line" && (
              <svg
                className="absolute left-0 top-0 h-full overflow-visible"
                width={plotWidth}
                viewBox={`0 0 ${plotWidth} 100`}
                preserveAspectRatio="none"
              >
                <polyline
                  points={linePoints}
                  fill="none"
                  stroke="var(--board-primary)"
                  strokeWidth={2}
                  strokeLinejoin="round"
                  vectorEffect="non-scaling-stroke"
                />
              </svg>
            )}
            <div className="relative flex h-full items-end gap-1 px-1">
              {data.map((d, i) => (
                <div
                  key={d.key}
                  className="group relative flex h-full w-8 shrink-0 flex-col items-center justify-end"
                  title={`${d.fullLabel}: ${d.value}`}
                >
                  {i === labelledIndex && (
                    <span
                      className={`absolute left-1/2 -translate-x-1/2 text-xs font-semibold text-slate-700 tabular-nums ${variant === "line" ? "pb-2" : "pb-1"}`}
                      style={{ bottom: `${percent(d.value)}%` }}
                    >
                      {d.value}
                    </span>
                  )}
                  {variant === "line" ? (
                    <span
                      className="absolute left-1/2 size-2.5 -translate-x-1/2 translate-y-1/2 rounded-full border-2 border-white bg-(--board-primary) transition-transform group-hover:scale-125"
                      style={{ bottom: `${percent(d.value)}%` }}
                    />
                  ) : (
                    <div
                      className="w-full rounded-t bg-(--board-primary) opacity-90 transition-opacity group-hover:opacity-100"
                      style={{
                        height: `${percent(d.value)}%`,
                        minHeight: d.value > 0 ? "2px" : 0,
                      }}
                    />
                  )}
                </div>
              ))}
            </div>
          </div>
          <div className="flex w-max min-w-full gap-1 px-1 pt-1">
            {data.map((d, i) => (
              <span
                key={d.key}
                className={`w-8 shrink-0 whitespace-nowrap text-center text-[0.65rem] tabular-nums ${i === labelledIndex ? "font-semibold text-slate-700" : "text-slate-400"}`}
              >
                {d.label}
              </span>
            ))}
          </div>
        </div>
      </div>
      <div className="sr-only">
        <table>
          <thead>
            <tr>
              <th scope="col">{categoryLabel ?? t("year")}</th>
              <th scope="col">{label}</th>
            </tr>
          </thead>
          <tbody>
            {data.map((d) => (
              <tr key={d.key}>
                <td>{d.fullLabel}</td>
                <td>{d.value}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </figure>
  );
}
