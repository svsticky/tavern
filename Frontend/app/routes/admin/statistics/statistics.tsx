import { t } from "i18next";
import { useEffect, useState } from "react";
import toast from "react-hot-toast";
import { useSearchParams } from "react-router";
import type { StatisticsResponseDto, YearCountDto } from "~/api";
import StatisticsChart from "~/components/Statistics/StatisticsChart";
import BorderedTile from "~/components/Tiles/BorderedTile";
import type { Column } from "~/components/Tiles/DataTableTile";
import DataTable from "~/components/Tiles/DataTableTile";
import { PageHeader } from "~/components/UI/PageHeader";
import Select from "~/components/UI/Select";
import { getCommitteeYear } from "~/util/date.util";
import { appendErrorMessage } from "~/util/error.util";
import {
  ACTIVITY_YEAR_PARAM,
  distributionToChartData,
  formatCommitteeYear,
  getFirstDataYear,
  getSelectedActivityYear,
  getSelectedYear,
  loadStatistics,
  mergeStudyRows,
  type StudyRow,
  yearCountsToChartData,
} from "./statistics.handlers";

type Series =
  | "total_members"
  | "new_members"
  | "new_bachelor_members"
  | "new_master_members"
  | "active_members"
  | "first_years_at_activities";

/**
 * A count with a thin, fixed-width bar showing its size relative to the largest count in the column.
 */
function CountBar({ value, max }: { value: number; max: number }) {
  // Built from spans on purpose: DataTable stretches every div in its last column to full width.
  return (
    <span className="flex items-center gap-3">
      <span className="w-10 text-right tabular-nums">{value}</span>
      <span
        className="block h-1.5 w-24 shrink-0 rounded-full bg-slate-100"
        aria-hidden="true"
      >
        <span
          className="block h-full rounded-full bg-(--board-primary)"
          style={{ width: `${max > 0 ? (value / max) * 100 : 0}%` }}
        />
      </span>
    </span>
  );
}

/**
 * The administrative Statistics page.
 *
 * It shows the history of one chosen series for all years (the number of members as a line; new,
 * active members and first-years at activities as columns), how many activities the
 * members of a chosen year (`?activityYear=`) attended, and per study the active
 * enrollments and that year's new and active members. The year of the per-study table is
 * selected there (default: the current year with the data up to now, older years down to the oldest
 * year with data) and kept in the URL (`?year=`). Only board and candidate board members can open
 * this page (see the admin layout).
 *
 * All years are committee years (starting on the CommitteeCreationDate setting); deleted accounts
 * are included in the historical numbers.
 *
 * @page
 * @component
 */
export default function Statistics() {
  const [searchParams, setSearchParams] = useSearchParams();
  const committeeYear = getCommitteeYear();
  const selectedYear = getSelectedYear(searchParams, committeeYear);
  const [series, setSeries] = useState<Series>("total_members");
  // The statistics together with the year they were loaded for, so the page keeps showing a
  // consistent year while the next year loads.
  const [loaded, setLoaded] = useState<{
    statistics: StatisticsResponseDto;
    year: number;
  } | null>(null);
  const [loading, setLoading] = useState(true);

  // Only the per-study year reloads the statistics: all years of the activity distribution are
  // already loaded, so changing that year only changes the URL.
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    loadStatistics(selectedYear, committeeYear)
      .then((statistics) => {
        if (!cancelled) setLoaded({ statistics, year: selectedYear });
      })
      .catch((error) => {
        console.error("Error loading the statistics:", error);
        if (!cancelled)
          toast.error(appendErrorMessage(t("loading_failed"), error));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [selectedYear, committeeYear]);

  if (!loaded) {
    return loading ? (
      t("loading")
    ) : (
      <>
        <PageHeader title={t("statistics")} backTo="/" />
        <p className="text-gray-500">{t("loading_failed")}</p>
      </>
    );
  }

  const { statistics, year } = loaded;

  const firstYear = Math.min(getFirstDataYear(statistics, committeeYear), year);
  const selectableYears = Array.from(
    { length: committeeYear - firstYear + 1 },
    (_, i) => committeeYear - i,
  );

  /**
   * Shows the statistics of another committee year. The current year is the default, so it
   * removes the parameter instead of setting it.
   *
   * @param {number} selected - The committee year to show.
   */
  const handleYearChange = (selected: number) => {
    setSearchParams((params) => {
      if (selected === committeeYear) params.delete("year");
      else params.set("year", String(selected));
      return params;
    });
  };

  const distributions = statistics.activitiesPerInvolvedMember ?? [];
  const activityYears = distributions.map((d) => d.year).sort((a, b) => b - a);
  const activityYear = getSelectedActivityYear(
    searchParams,
    activityYears,
    committeeYear,
  );

  /**
   * Shows the activity distribution of another committee year. Only the URL changes, the
   * statistics aren't reloaded.
   *
   * @param {number} selected - The committee year to show.
   */
  const handleActivityYearChange = (selected: number) => {
    setSearchParams(
      (params) => {
        params.set(ACTIVITY_YEAR_PARAM, String(selected));
        return params;
      },
      { preventScrollReset: true },
    );
  };

  const seriesData: Record<
    Series,
    {
      values: YearCountDto[] | null | undefined;
      description: string;
      variant?: "line";
    }
  > = {
    total_members: {
      values: statistics.totalMembersPerYear,
      description: t("total_members_per_year_subtitle"),
      variant: "line",
    },
    new_members: {
      values: statistics.newMembersPerYear,
      description: t("new_members_per_year_subtitle"),
    },
    new_bachelor_members: {
      values: statistics.newBachelorMembersPerYear,
      description: t("new_members_per_year_subtitle"),
    },
    new_master_members: {
      values: statistics.newMasterMembersPerYear,
      description: t("new_members_per_year_subtitle"),
    },
    active_members: {
      values: statistics.activeMembersPerYear,
      description: t("active_members_per_year_subtitle"),
    },
    first_years_at_activities: {
      values: statistics.firstYearsAtActivitiesPerYear,
      description: t("first_years_at_activities_per_year_subtitle"),
    },
  };

  const isCurrentYear = year === committeeYear;
  const yearLabel = formatCommitteeYear(year);

  const studyRows = mergeStudyRows({
    enrollments: statistics.activeEnrollmentsPerStudy,
    newMembers: statistics.newMembersPerStudy?.find((y) => y.year === year)
      ?.studies,
    activeMembers: statistics.activeMembersPerStudy?.find(
      (y) => y.year === year,
    )?.studies,
    newActiveMembers: statistics.newActiveMembersPerStudy?.find(
      (y) => y.year === year,
    )?.studies,
  });
  const maxEnrolled = Math.max(0, ...studyRows.map((r) => r.enrolled));
  const maxNew = Math.max(0, ...studyRows.map((r) => r.newMembers));
  const maxActive = Math.max(0, ...studyRows.map((r) => r.activeMembers));
  const maxNewActive = Math.max(0, ...studyRows.map((r) => r.newActiveMembers));
  const studyColumns: Column<StudyRow>[] = [
    { header: t("study"), render: (r) => r.title },
    {
      header: t("active_enrollments"),
      render: (r) => <CountBar value={r.enrolled} max={maxEnrolled} />,
    },
    {
      header: t("new_members"),
      render: (r) => <CountBar value={r.newMembers} max={maxNew} />,
    },
    {
      header: t("active_members"),
      render: (r) => <CountBar value={r.activeMembers} max={maxActive} />,
    },
    {
      header: t("new_active_members"),
      render: (r) => <CountBar value={r.newActiveMembers} max={maxNewActive} />,
    },
  ];

  return (
    <>
      <PageHeader title={t("statistics")} backTo="/" />

      <div
        className={`flex flex-col gap-4 w-full transition-opacity ${loading ? "opacity-60" : ""}`}
        aria-busy={loading}
      >
        <BorderedTile
          title={t("statistics_per_year")}
          subtitle={seriesData[series].description}
        >
          <div className="w-full sm:w-auto sm:self-end">
            <Select
              label={t("statistics_series")}
              style={{ minWidth: "220px" }}
              value={series}
              options={(Object.keys(seriesData) as Series[]).map((s) => ({
                value: s,
                label: t(s),
              }))}
              onChange={(e) => setSeries(e.target.value as Series)}
            />
          </div>
          <StatisticsChart
            label={t(series)}
            data={yearCountsToChartData(seriesData[series].values)}
            variant={seriesData[series].variant}
          />
        </BorderedTile>

        <BorderedTile
          title={t("statistics_activity_distribution")}
          subtitle={
            activityYear === null
              ? undefined
              : t("statistics_activity_distribution_subtitle", {
                  year: formatCommitteeYear(activityYear),
                })
          }
        >
          {activityYear !== null && (
            <div className="w-full sm:w-auto sm:self-end">
              <Select
                label={t("year")}
                style={{ minWidth: "150px" }}
                value={activityYear}
                options={activityYears.map((y) => ({
                  value: y,
                  label: formatCommitteeYear(y),
                }))}
                onChange={(e: React.ChangeEvent<HTMLSelectElement>) =>
                  handleActivityYearChange(Number.parseInt(e.target.value, 10))
                }
              />
            </div>
          )}
          <StatisticsChart
            label={t("members")}
            categoryLabel={t("activities")}
            emphasis="max"
            data={distributionToChartData(
              distributions.find((d) => d.year === activityYear)?.distribution,
            )}
          />
        </BorderedTile>

        <BorderedTile
          title={t("statistics_per_study")}
          subtitle={t(
            isCurrentYear
              ? "statistics_per_study_subtitle_now"
              : "statistics_per_study_subtitle_end_of_year",
            { year: yearLabel },
          )}
        >
          <div className="w-full sm:w-auto sm:self-end">
            <Select
              label={t("year")}
              style={{ minWidth: "150px" }}
              value={year}
              options={selectableYears.map((y) => ({
                value: y,
                label: formatCommitteeYear(y),
              }))}
              onChange={(e: React.ChangeEvent<HTMLSelectElement>) =>
                handleYearChange(Number.parseInt(e.target.value, 10))
              }
            />
          </div>
          <DataTable data={studyRows} columns={studyColumns} />
        </BorderedTile>
      </div>
    </>
  );
}
