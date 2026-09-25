using Microsoft.EntityFrameworkCore;
using Platform.Application.Messaging;
using Platform.Modules.Events.Contracts;
using Platform.Modules.People.Domain;
using Platform.Modules.People.Infrastructure;

namespace Platform.Modules.People.Features;

/// <summary>
/// First-time visitor → open a follow-up task for the pastoral team (and record the visit date).
/// Idempotent: never creates a second open first-timer follow-up for the same person.
/// </summary>
internal sealed class FirstTimerFollowUp(PeopleDbContext db) : IEventHandler<AttendanceRecordedIntegrationEvent>
{
    public async Task Handle(AttendanceRecordedIntegrationEvent e, CancellationToken cancellationToken)
    {
        if (!e.IsFirstVisit)
        {
            return;
        }

        var exists = await db.FollowUps.AnyAsync(f =>
            f.PersonId == e.PersonId && f.Type == FollowUpType.FirstTimeVisitor &&
            (f.Status == FollowUpStatus.Open || f.Status == FollowUpStatus.InProgress), cancellationToken);
        var personExists = await db.People.AnyAsync(p => p.Id == e.PersonId, cancellationToken);
        if (exists || !personExists)
        {
            return;
        }

        db.FollowUps.Add(FollowUp.Create(e.PersonId, FollowUpType.FirstTimeVisitor, Priority.High, assignedTo: null,
            DateOnly.FromDateTime(e.CheckedInAt.UtcDateTime.AddDays(3)), $"First visit: {e.EventTitle} on {e.CheckedInAt:dd MMM yyyy}."));
        await db.SaveChangesAsync(cancellationToken);
    }
}
