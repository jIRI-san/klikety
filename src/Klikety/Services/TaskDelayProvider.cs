namespace Klikety.Services;

public sealed class TaskDelayProvider : IDelayProvider {
    public Task Delay(int milliseconds, CancellationToken ct) =>
        Task.Delay(milliseconds, ct);
}
