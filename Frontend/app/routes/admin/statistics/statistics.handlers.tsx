import { t } from "i18next";
import {
  type ActivityCountDto,
  getStatistics,
  type StatisticsResponseDto,
  type StudyCountDto,
  type YearCountDto,
} from "~/api";

/**
 * A single labelled value as consumed by the statistics charts.
 */
export type ChartDatum = {
  /** Stable key for React lists. */
  key: string;
  /** Short label shown on the axis. */
  label: string;
  /** Full label shown in tooltips and screen-reader tables. */
  fullLabel: string;
  value: number;
};

/**
 * Reads the selected committee year from the `year` search parameter. Falls back to
 * the current committee year when it's missing, invalid or in the future.
 *
 * @param {URLSearchParams} searchParams - The search parameters of the page URL.
 * @param {number} committeeYear - The current committee year.
 * @returns {number} The selected committee year.
 */
export const getSelectedYear = (
  searchParams: URLSearchParams,
  committeeYear: number,
): number => {
  const urlYear = Number.parseInt(searchParams.get("year") ?? "", 10);
  return Number.isInteger(urlYear) && urlYear > 0 && urlYear <= committeeYear
    ? urlYear
    : committeeYear;
};

/**
 * The oldest committee year that has any statistics data (new members, active members,
 * first-years at activities), so the year selector starts where the data starts instead of
 * at a fixed year. Falls back to `fallback` when there is no data at all.
 *
 * @param {StatisticsResponseDto} statistics - The loaded statistics.
 * @param {number} fallback - The year to use when there is no data (the current committee year).
 * @returns {number} The oldest committee year with data.
 */
export const getFirstDataYear = (
  statistics: StatisticsResponseDto,
  fallback: number,
): number => {
  const years = [
    statistics.totalMembersPerYear,
    statistics.newMembersPerYear,
    statistics.newBachelorMembersPerYear,
    statistics.newMasterMembersPerYear,
    statistics.activeMembersPerYear,
    statistics.firstYearsAtActivitiesPerYear,
    statistics.activeMembersPerStudy,
    statistics.newMembersPerStudy,
    statistics.newActiveMembersPerStudy,
  ].flatMap((series) => (series ?? []).map((entry) => entry.year));
  return years.length > 0 ? Math.min(...years) : fallback;
};

/**
 * Loads all statistics, with the point-in-time statistics for the given year (the current
 * year, with the data up to now, when it is the current committee year).
 *
 * @param {number} year - The selected committee year.
 * @param {number} committeeYear - The current committee year.
 * @returns {Promise<StatisticsResponseDto>} All statistics.
 * @throws {Error} When the statistics fail to load.
 */
export const loadStatistics = async (
  year: number,
  committeeYear: number,
): Promise<StatisticsResponseDto> => {
  const statisticsResponse = await getStatistics(
    year === committeeYear ? undefined : { query: { year } },
  );

  if (statisticsResponse.error || !statisticsResponse.data)
    throw statisticsResponse.error ?? new Error("Failed to load statistics");

  return statisticsResponse.data;
};

/**
 * Formats a committee year (e.g. `2025`) as the academic year it spans (`2024-2025`),
 * the same notation as the year selectors elsewhere in the admin pages.
 */
export const formatCommitteeYear = (year: number, short = false): string =>
  short
    ? `${String(year - 1).slice(-2)}-${String(year).slice(-2)}`
    : `${year - 1}-${year}`;

/**
 * Converts a per-year series into chart data, filling in missing years between the
 * first and last year with zero so gaps are visible instead of silently skipped.
 */
export const yearCountsToChartData = (
  counts: YearCountDto[] | null | undefined,
): ChartDatum[] => {
  if (!counts || counts.length === 0) return [];

  const byYear = new Map(counts.map((c) => [c.year, c.count]));
  const years = counts.map((c) => c.year);
  const first = Math.min(...years);
  const last = Math.max(...years);

  const data: ChartDatum[] = [];
  for (let year = first; year <= last; year++) {
    data.push({
      key: String(year),
      label: formatCommitteeYear(year, true),
      fullLabel: formatCommitteeYear(year),
      value: byYear.get(year) ?? 0,
    });
  }
  return data;
};

/**
 * Converts per-study counts into chart data. Entries without a study are labelled
 * with the translated "no study" label.
 */
export const studyCountsToChartData = (
  counts: StudyCountDto[] | null | undefined,
): ChartDatum[] =>
  (counts ?? []).map((c) => {
    const label = c.studyTitle ?? t("no_study");
    return {
      key: String(c.studyId ?? "none"),
      label,
      fullLabel: label,
      value: c.count,
    };
  });

/**
 * A row of the "per study" table.
 * @typedef {Object} StudyRow
 * @property {string} key - Stable key for React lists.
 * @property {string} title - The study title, or the translated "no study" label.
 * @property {number} enrolled - Active study enrollments at the snapshot moment.
 * @property {number} newMembers - New members of the selected year that followed the study when they counted as new.
 * @property {number} activeMembers - Active members (in a non-dispute group) of the selected year following the study.
 * @property {number} newActiveMembers - New members of the selected year that were also active that year, by the study they followed when they counted as new.
 */
export type StudyRow = {
  key: string;
  title: string;
  enrolled: number;
  newMembers: number;
  activeMembers: number;
  newActiveMembers: number;
};

/**
 * The per-study counts that make up the "per study" table.
 * @typedef {Object} StudyRowSources
 * @property {StudyCountDto[] | null | undefined} enrollments - Active enrollments per study.
 * @property {StudyCountDto[] | null | undefined} newMembers - New members per study in the selected year.
 * @property {StudyCountDto[] | null | undefined} activeMembers - Active members per study in the selected year.
 * @property {StudyCountDto[] | null | undefined} [newActiveMembers] - New members per study in the selected year that were also active that year.
 */
type StudyRowSources = {
  enrollments: StudyCountDto[] | null | undefined;
  newMembers: StudyCountDto[] | null | undefined;
  activeMembers: StudyCountDto[] | null | undefined;
  newActiveMembers?: StudyCountDto[] | null;
};

/**
 * Combines the per-study counts into one row per study, sorted by enrollments, then by
 * new members, then by active members.
 *
 * @param {StudyRowSources} sources - The per-study counts.
 * @returns {StudyRow[]} One row per study that has a count in any of the sources.
 */
export const mergeStudyRows = ({
  enrollments,
  newMembers,
  activeMembers,
  newActiveMembers,
}: StudyRowSources): StudyRow[] => {
  const rows = new Map<string, StudyRow>();
  const row = (c: StudyCountDto) => {
    const key = String(c.studyId ?? "none");
    const existing = rows.get(key);
    if (existing) return existing;
    const created = {
      key,
      title: c.studyTitle ?? t("no_study"),
      enrolled: 0,
      newMembers: 0,
      activeMembers: 0,
      newActiveMembers: 0,
    };
    rows.set(key, created);
    return created;
  };
  for (const c of enrollments ?? []) row(c).enrolled += c.count;
  for (const c of newMembers ?? []) row(c).newMembers += c.count;
  for (const c of activeMembers ?? []) row(c).activeMembers += c.count;
  for (const c of newActiveMembers ?? []) row(c).newActiveMembers += c.count;

  return [...rows.values()]
    .filter((r) => r.enrolled > 0 || r.newMembers > 0 || r.activeMembers > 0)
    .sort(
      (a, b) =>
        b.enrolled - a.enrolled ||
        b.newMembers - a.newMembers ||
        b.activeMembers - a.activeMembers ||
        a.title.localeCompare(b.title),
    );
};

/** At most this many columns in the activity distribution; beyond that, counts are grouped into ranges. */
export const MAX_DISTRIBUTION_COLUMNS = 30;

/**
 * Converts the distribution of attended activities into chart data: one column per number of
 * activities from 1 up to the highest number, with missing numbers as 0 so the shape has no gaps.
 * When that would be more than {@link MAX_DISTRIBUTION_COLUMNS} columns, consecutive numbers are
 * grouped into ranges of equal width (e.g. "1-3", "4-6"), so one very active member doesn't make
 * the chart extremely long.
 *
 * @param {ActivityCountDto[] | null | undefined} distribution - Members per number of attended activities.
 * @returns {ChartDatum[]} The chart data, ascending by number of activities.
 */
export const distributionToChartData = (
  distribution: ActivityCountDto[] | null | undefined,
): ChartDatum[] => {
  if (!distribution || distribution.length === 0) return [];

  const highest = Math.max(...distribution.map((d) => d.activities));
  const width = Math.ceil(highest / MAX_DISTRIBUTION_COLUMNS);
  const membersPerColumn = new Map<number, number>();
  for (const d of distribution) {
    const column = Math.floor((d.activities - 1) / width);
    membersPerColumn.set(
      column,
      (membersPerColumn.get(column) ?? 0) + d.members,
    );
  }

  return Array.from({ length: Math.ceil(highest / width) }, (_, column) => {
    const from = column * width + 1;
    const to = Math.min(from + width - 1, highest);
    const label = from === to ? String(from) : `${from}-${to}`;
    return {
      key: label,
      label,
      fullLabel:
        from === to
          ? t("statistics_activities_count", { count: from })
          : t("statistics_activities_range", { from, to }),
      value: membersPerColumn.get(column) ?? 0,
    };
  });
};

/** The search parameter with the selected year of the activity distribution. */
export const ACTIVITY_YEAR_PARAM = "activityYear";

/**
 * Reads the selected year of the activity distribution from the URL. Falls back to the current
 * committee year when it has data, otherwise to the most recent year with data.
 *
 * @param {URLSearchParams} searchParams - The page's search parameters.
 * @param {number[]} years - The years that have a distribution.
 * @param {number} committeeYear - The current committee year.
 * @returns {number | null} The selected year, or null when no year has data.
 */
export const getSelectedActivityYear = (
  searchParams: URLSearchParams,
  years: number[],
  committeeYear: number,
): number | null => {
  const urlYear = Number.parseInt(
    searchParams.get(ACTIVITY_YEAR_PARAM) ?? "",
    10,
  );
  if (years.includes(urlYear)) return urlYear;
  if (years.includes(committeeYear)) return committeeYear;
  return years.length > 0 ? Math.max(...years) : null;
};
