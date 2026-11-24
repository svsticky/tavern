using Backend.Models.Domain;

namespace Backend.Controllers.DTOs;

/// <summary>
/// Identifies a single statistic that can be requested from the statistics endpoint. Every value maps to exactly one property of <see cref="StatisticsResponseDTO"/>.
/// </summary>
public enum StatisticType
{
    /// <summary>The number of members per committee year: at the end of each past year, and now for the current year. A member is someone with an active study enrollment, or an honorary member, "Lid van Verdienste" or gratie member.</summary>
    TotalMembersPerYear,

    /// <summary>The number of study enrollments per study that are active at the snapshot moment.</summary>
    ActiveEnrollmentsPerStudy,

    /// <summary>The number of new members per committee year: members that ever followed a study, or are a gratie member, honorary member or "Lid van Verdienste", counted in the year they registered, or in the year their first study starts if that is later.</summary>
    NewMembersPerYear,

    /// <summary>The number of new bachelor members per committee year, where a member counts as new in max(registration year, year of their first bachelor enrollment).</summary>
    NewBachelorMembersPerYear,

    /// <summary>The number of new master members per committee year, where a member counts as new in max(registration year, year of their first master enrollment).</summary>
    NewMasterMembersPerYear,

    /// <summary>The number of unique members per committee year that are part of at least one group, disputes excluded.</summary>
    ActiveMembersPerYear,

    /// <summary>Per committee year, a histogram of the studies followed by the active members (in at least one non-dispute group) of that year.</summary>
    ActiveMembersPerStudy,

    /// <summary>Per committee year, how many members attended exactly 1, 2, 3, ... activities that year (confirmed, not on the waiting list). Members without any activity are not included.</summary>
    ActivitiesPerInvolvedMember,

    /// <summary>Per committee year, the new members of that year (see <see cref="NewMembersPerYear"/>) per study they followed at the moment they counted as new.</summary>
    NewMembersPerStudy,

    /// <summary>The number of unique first-years per committee year (all members that are new in that year, see <see cref="NewMembersPerYear"/>) that attended at least one activity in that year.</summary>
    FirstYearsAtActivitiesPerYear,

    /// <summary>Per committee year, the new members of that year (see <see cref="NewMembersPerYear"/>) that were also active that year (in at least one non-dispute group), per study they followed at the moment they counted as new.</summary>
    NewActiveMembersPerStudy
}

/// <summary>
/// A count belonging to a single committee year. The year follows the backend convention of <see cref="Utils.DateTime.YearUtils.GetYearForDate"/> with the CommitteeCreationDate setting as start date.
/// </summary>
public class YearCountDTO
{
    /// <summary>The committee year.</summary>
    public required uint Year { get; set; }

    /// <summary>The count for this year.</summary>
    public required int Count { get; set; }
}

/// <summary>
/// A count belonging to a single study. When <see cref="StudyId"/> is null, the count represents members without a (matching) study.
/// </summary>
public class StudyCountDTO
{
    /// <summary>The id of the study, or null for members without a study.</summary>
    public uint? StudyId { get; set; }

    /// <summary>The title of the study, or null for members without a study.</summary>
    public string? StudyTitle { get; set; }

    /// <summary>The type of the study, or null for members without a study.</summary>
    public StudyType? StudyType { get; set; }

    /// <summary>The count for this study.</summary>
    public required int Count { get; set; }
}

/// <summary>
/// The per-study counts for a single committee year.
/// </summary>
public class YearStudyCountsDTO
{
    /// <summary>The committee year.</summary>
    public required uint Year { get; set; }

    /// <summary>The counts per study in this year.</summary>
    public required List<StudyCountDTO> Studies { get; set; }
}

/// <summary>
/// The number of members that attended exactly <see cref="Activities"/> activities.
/// </summary>
public class ActivityCountDTO
{
    /// <summary>The number of activities attended.</summary>
    public required int Activities { get; set; }

    /// <summary>The number of members that attended exactly that many activities.</summary>
    public required int Members { get; set; }
}

/// <summary>
/// The distribution of attended activities over the involved members of a single committee year.
/// </summary>
public class YearActivityDistributionDTO
{
    /// <summary>The committee year.</summary>
    public required uint Year { get; set; }

    /// <summary>The number of members per number of attended activities, ascending by activities. Only counts that occur are listed.</summary>
    public required List<ActivityCountDTO> Distribution { get; set; }
}

/// <summary>
/// The collection of statistics shown on the administrative statistics page. When a single statistic is requested, only the corresponding property is populated and all other properties are null. Deleted (anonymized) member accounts are included in all historical statistics.
/// </summary>
public class StatisticsResponseDTO
{
    /// <summary>The committee year the point-in-time statistic <see cref="ActiveEnrollmentsPerStudy"/> refers to.</summary>
    public required uint Year { get; set; }

    /// <summary>The moment the point-in-time statistic refers to: now for the current committee year, otherwise the end of the requested year.</summary>
    public required DateTimeOffset SnapshotAt { get; set; }

    /// <inheritdoc cref="StatisticType.TotalMembersPerYear"/>
    public List<YearCountDTO>? TotalMembersPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.ActiveEnrollmentsPerStudy"/>
    public List<StudyCountDTO>? ActiveEnrollmentsPerStudy { get; set; }

    /// <inheritdoc cref="StatisticType.NewMembersPerYear"/>
    public List<YearCountDTO>? NewMembersPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.NewBachelorMembersPerYear"/>
    public List<YearCountDTO>? NewBachelorMembersPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.NewMasterMembersPerYear"/>
    public List<YearCountDTO>? NewMasterMembersPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.ActiveMembersPerYear"/>
    public List<YearCountDTO>? ActiveMembersPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.ActiveMembersPerStudy"/>
    public List<YearStudyCountsDTO>? ActiveMembersPerStudy { get; set; }

    /// <inheritdoc cref="StatisticType.ActivitiesPerInvolvedMember"/>
    public List<YearActivityDistributionDTO>? ActivitiesPerInvolvedMember { get; set; }

    /// <inheritdoc cref="StatisticType.NewMembersPerStudy"/>
    public List<YearStudyCountsDTO>? NewMembersPerStudy { get; set; }

    /// <inheritdoc cref="StatisticType.FirstYearsAtActivitiesPerYear"/>
    public List<YearCountDTO>? FirstYearsAtActivitiesPerYear { get; set; }

    /// <inheritdoc cref="StatisticType.NewActiveMembersPerStudy"/>
    public List<YearStudyCountsDTO>? NewActiveMembersPerStudy { get; set; }
}
