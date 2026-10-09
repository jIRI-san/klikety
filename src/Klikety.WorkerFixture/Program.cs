using System.IO;

using Klikety.Automation;

if (args[0] == "startup-hang") { await Task.Delay(Timeout.Infinite); }
if (args[0] == "ui") {
    UiaFixture.Run();
    return;
}
if (args[0] == "parent") {
    var owned = new UiaWorkerSupervisor(Environment.ProcessPath!, "hang");
    _ = owned.DiscoverAsync(new(1, Environment.ProcessId), new(0, 0, 100, 100), CancellationToken.None);
    for (int i = 0; i < 100 && owned.OwnedProcessId is null; i++) { await Task.Delay(10); }
    File.WriteAllText(args[1], owned.OwnedProcessId?.ToString() ?? "0");
    Environment.Exit(0);
}
var input = Console.OpenStandardInput();
var output = Console.OpenStandardOutput();
var request = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
if (args[0].StartsWith("cold-", StringComparison.Ordinal)) {
    int discoveries = 0;
    while (true) {
        if (request.Command == HintCommand.Discover) { discoveries++; }
        if (args[0] == "cold-delay") { await Task.Delay(300); }
        var target = new HintTarget(1, [42, 1], 42, 50000, HintCapabilities.Invoke,
            new(10, 10, 20, 20), new(10, 10, 20, 20), new(20, 20));
        bool recovered = discoveries > 1 && args[0] is not ("cold-empty" or "cold-delay");
        var outcome = recovered ? HintOutcome.Success :
            args[0] == "cold-error" ? HintOutcome.ProviderError : HintOutcome.NoTargets;
        await ElementHintProtocol.WriteAsync(output, new HintResponse(1, request.SessionId, request.RequestId,
            outcome, recovered ? [target] : [], Visited: 1, Reason: $"discoveries={discoveries}", RootProcessId: 42),
            ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        try {
            request = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        } catch (EndOfStreamException) { return; }
    }
}
if (args[0] == "recover" && !File.Exists(args[1])) { await Task.Delay(Timeout.Infinite); }
if (args[0] == "delay") { await Task.Delay(int.Parse(args[1])); }
if (args[0].StartsWith("incremental", StringComparison.Ordinal)) {
    int round = 0;
    var target = new HintTarget(1, [42, 1], 42, 50000, HintCapabilities.Invoke,
        new(10, 10, 20, 20), new(10, 10, 20, 20), new(20, 20));
    while (true) {
        HintResponse response;
        if (request.Command == HintCommand.Validate) {
            response = new(1, request.SessionId, request.RequestId, HintOutcome.Success, [],
                Point: new(20, 20), RootProcessId: 42);
        } else if (request.Command == HintCommand.Release) {
            response = new(1, request.SessionId, request.RequestId, HintOutcome.Success, []);
        } else {
            if (request.Command == HintCommand.Discover) { round = 0; } else { round++; }
            if (round > 0 && args[0] == "incremental-hang") { await Task.Delay(Timeout.Infinite); }
            var targets = round == 0 ? new[] { target } : new[] { target, target with { Token = 2, RuntimeId = [42, 2] } };
            if (round > 0 && args[0] == "incremental-remap") { targets[0] = target with { RuntimeId = [42, 99] }; }
            response = new(1, request.SessionId, request.RequestId, HintOutcome.Success, targets,
                RootProcessId: 42, IsComplete: round > 0 && args[0] != "incremental-endless");
            if (args[0] == "incremental-cache-settings") {
                response = response with { Reason = $"{request.CacheWindowCount}" };
            }
            if (args[0] == "incremental-unproductive") {
                response = response with {
                    Targets = round < 4 ? [] : round == 4 ? [target] :
                        [target, target with { Token = 2, RuntimeId = [42, 2] }],
                    IsComplete = round >= 5
                };
            }
        }
        await ElementHintProtocol.WriteAsync(output, response, ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        try {
            request = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        } catch (EndOfStreamException) { return; }
    }
}
switch (args[0]) {
    case "hang": await Task.Delay(Timeout.Infinite); break;
    case "crash": Environment.Exit(3); break;
    case "truncated":
        await output.WriteAsync(new byte[] { 10, 0, 0, 0, 0 });
        await output.FlushAsync();
        Environment.Exit(0); break;
    case "oversized": await output.WriteAsync(BitConverter.GetBytes(ElementHintProtocol.MaxResponseBytes + 1)); await Task.Delay(Timeout.Infinite); break;
    case "stderr": await Console.OpenStandardError().WriteAsync(new byte[ElementHintProtocol.MaxDiagnosticBytes + 1]); await Task.Delay(Timeout.Infinite); break;
    case "malformed": await output.WriteAsync(new byte[] { 1, 0, 0, 0, 0xff }); await Task.Delay(Timeout.Infinite); break;
    case "mismatch":
        await ElementHintProtocol.WriteAsync(output, new HintResponse(99, request.SessionId, request.RequestId,
            HintOutcome.NoTargets, []), ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        await Task.Delay(Timeout.Infinite); break;
    default:
        await ElementHintProtocol.WriteAsync(output, new HintResponse(1, request.SessionId, request.RequestId,
            HintOutcome.NoTargets, []), ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        if (args[0] == "validation-intent") {
            while (true) {
                request = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
                await ElementHintProtocol.WriteAsync(output,
                    new HintResponse(ElementHintProtocol.Version, request.SessionId, request.RequestId, HintOutcome.Success, [],
                        Reason: request.MoveOnly ? "MoveOnly" : "DirectAction", Point: new(20, 20), RootProcessId: 42),
                    ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
            }
        }
        if (args[0] == "validation-hang") {
            _ = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        }
        await Task.Delay(Timeout.Infinite); break;
}
