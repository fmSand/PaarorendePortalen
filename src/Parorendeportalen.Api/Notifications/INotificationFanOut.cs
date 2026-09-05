namespace Parorendeportalen.Api.Notifications;

// Delivered counts rows written, so it can exceed Processed when one change
// reaches several people.
public sealed record FanOutResult(int Processed, int Delivered);

public interface INotificationFanOut
{
    Task<FanOutResult> DeliverPendingAsync(CancellationToken cancellationToken);
}
