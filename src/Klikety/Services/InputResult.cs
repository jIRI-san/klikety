namespace Klikety.Services;

public enum InputStage {
    Move,
    Click,
    Scroll,
    DragStart,
    DragNudge,
    DragEnd,
    ClearModifiers,
    ReleaseCleanup,
}

public sealed record InputSendOutcome(InputStage Stage, int Requested, uint Sent, int? NativeError = null) {
    public bool Succeeded => Sent == Requested;
    public override string ToString() =>
        $"{Stage}: {Sent}/{Requested}, native error: {NativeError?.ToString() ?? "unavailable (including possible UIPI rejection)"}";
}

public sealed record InputResult(IReadOnlyList<InputSendOutcome> Sends, InputSendOutcome? Cleanup = null) {
    public bool Succeeded => Sends.Count > 0 && Sends.All(send => send.Succeeded) && (Cleanup?.Succeeded ?? true);
    public InputSendOutcome? Failure => Sends.FirstOrDefault(send => !send.Succeeded);
    public override string ToString() =>
        $"{Failure?.ToString() ?? (Sends.Count == 0 ? "Input failed: no send outcomes" : "Input completed")}{(Cleanup is null ? "" : $"; release cleanup: {Cleanup}")}";
}
