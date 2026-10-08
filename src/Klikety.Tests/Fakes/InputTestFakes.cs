using System.Drawing;

using Klikety.Services;

using Microsoft.Extensions.Logging;

namespace Klikety.Tests.Fakes;

internal sealed class CapturingLogger : ILogger {
    public List<(LogLevel Level, string Message)> Entries { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, formatter(state, exception)));
}

internal sealed class FakeInputSender : MouseActionService.IInputSender {
    public List<MouseActionService.INPUT[]> Batches { get; } = [];
    public Dictionary<int, MouseActionService.NativeSendResult> Results { get; } = [];
    public MouseActionService.NativeSendResult Send(MouseActionService.INPUT[] inputs) {
        Batches.Add(inputs);
        return Results.GetValueOrDefault(Batches.Count, new MouseActionService.NativeSendResult((uint)inputs.Length, null));
    }
    public MouseActionService CreateService(IDelayProvider? delay = null, ILogger? logger = null) =>
        new(logger ?? new CapturingLogger(), this, () => new Rectangle(-1920, -1080, 3840, 2160), delay ?? new FakeDelayProvider());
}
