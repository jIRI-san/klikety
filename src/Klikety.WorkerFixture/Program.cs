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
if (args[0] == "recover" && !File.Exists(args[1])) { await Task.Delay(Timeout.Infinite); }
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
        if (args[0] == "validation-hang") {
            _ = await ElementHintProtocol.ReadAsync<HintRequest>(input, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        }
        await Task.Delay(Timeout.Infinite); break;
}
