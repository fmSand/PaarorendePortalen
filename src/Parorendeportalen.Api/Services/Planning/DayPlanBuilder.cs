using Parorendeportalen.Api.Models;
using Parorendeportalen.Api.Models.Planning;
using Parorendeportalen.Api.Models.Visits;

namespace Parorendeportalen.Api.Services.Planning;

// Expands the vedtak in force into the occurrences they grant on this date, then lets
// the reported visits settle which happened. Pure, so it is testable without a database.
public static class DayPlanBuilder
{
    public static DayPlan Build(
        int careRecipientId,
        DateOnly date,
        IReadOnlyList<Vedtak> vedtak,
        IReadOnlyList<Visit> visits
    )
    {
        ArgumentNullException.ThrowIfNull(vedtak);
        ArgumentNullException.ThrowIfNull(visits);
        GuardScope(careRecipientId, vedtak, visits);

        var granted = GrantedOccurrences(date, vedtak);
        var reported = ReportedVisits(date, visits);

        var items = new List<DayPlanItem>();

        foreach (var serviceType in granted.Keys.Union(reported.Keys))
        {
            var occurrences = granted.GetValueOrDefault(serviceType, []);
            var visitsOfType = reported.GetValueOrDefault(serviceType, []);

            // A visit the vedtak did not grant still happened, so the longer of
            // the two decides how many items the day holds.
            for (var index = 0; index < Math.Max(occurrences.Count, visitsOfType.Count); index++)
            {
                var occurrence = index < occurrences.Count ? occurrences[index] : null;
                var visit = index < visitsOfType.Count ? visitsOfType[index] : null;

                items.Add(
                    new DayPlanItem(
                        serviceType,
                        occurrence?.Title,
                        index + 1,
                        StatusOf(visit),
                        occurrence?.VedtakId,
                        visit?.Id,
                        visit?.ScheduledAt,
                        visit?.ActualAt,
                        visit?.CaregiverName
                    )
                );
            }
        }

        return new DayPlan(careRecipientId, date, Ordered(items));
    }

    // Chronological where a time is known. An unbooked occurrence has none and sorts
    // after the booked ones.
    private static List<DayPlanItem> Ordered(List<DayPlanItem> items) =>
        [
            .. items
                .OrderBy(item => item.ScheduledAt is null)
                .ThenBy(item => item.ScheduledAt)
                .ThenBy(item => item.ServiceType)
                .ThenBy(item => item.Occurrence),
        ];

    private static DayPlanItemStatus StatusOf(Visit? visit) =>
        visit?.Status switch
        {
            null => DayPlanItemStatus.Expected,
            VisitStatus.Planned => DayPlanItemStatus.Planned,
            VisitStatus.Completed => DayPlanItemStatus.Completed,
            VisitStatus.Missed => DayPlanItemStatus.Missed,
            VisitStatus.Cancelled => DayPlanItemStatus.Cancelled,
            _ => throw new ArgumentOutOfRangeException(
                nameof(visit),
                visit.Status,
                "No day plan status covers this visit status."
            ),
        };

    // Two vedtak can grant the same service on one day, so occurrences are pooled per
    // service and numbered once across the day.
    private static Dictionary<ServiceType, List<GrantedOccurrence>> GrantedOccurrences(
        DateOnly date,
        IReadOnlyList<Vedtak> vedtak
    )
    {
        var granted = new Dictionary<ServiceType, List<GrantedOccurrence>>();

        var due = vedtak
            .Where(v => v.IsInForceOn(date) && v.Recurrence.Covers(date))
            .OrderBy(v => v.ServiceType)
            .ThenBy(v => v.Id);

        foreach (var v in due)
        {
            var occurrences = granted.TryGetValue(v.ServiceType, out var existing)
                ? existing
                : granted[v.ServiceType] = [];

            for (var i = 0; i < v.Recurrence.TimesPerDay; i++)
            {
                occurrences.Add(new GrantedOccurrence(v.Id, v.Title));
            }
        }

        return granted;
    }

    // No service type means a portal-authored visit or a source that predates the
    // field. Neither is a municipal service, so neither settles an occurrence of one.
    private static Dictionary<ServiceType, List<Visit>> ReportedVisits(
        DateOnly date,
        IReadOnlyList<Visit> visits
    ) =>
        visits
            .Where(v => v.ServiceType is not null && NorwegianTime.DateOf(v.ScheduledAt) == date)
            .OrderBy(v => v.ScheduledAt)
            .ThenBy(v => v.Id)
            .GroupBy(v => v.ServiceType!.Value)
            .ToDictionary(group => group.Key, group => group.ToList());

    // A row for someone else reaching this far is an authorisation bug, and
    // silently filtering it would leave the day plan looking merely incomplete.
    private static void GuardScope(
        int careRecipientId,
        IReadOnlyList<Vedtak> vedtak,
        IReadOnlyList<Visit> visits
    )
    {
        if (
            vedtak.Any(v => v.CareRecipientId != careRecipientId)
            || visits.Any(v => v.CareRecipientId != careRecipientId)
        )
        {
            throw new ArgumentException(
                $"A day plan for care recipient {careRecipientId} was given rows belonging to another."
            );
        }
    }

    private sealed record GrantedOccurrence(int VedtakId, string Title);
}
