using System.IO;

using Klikety.Automation;

namespace Klikety.Tests;

public class ElementHintProtocolTests {
    [Theory]
    [InlineData(long.MaxValue)]
    [InlineData(0xffffffff)]
    [InlineData(-1)]
    public async Task HwndAndRuntimeIdentityRoundTripWithoutTruncation(long hwnd) {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Discover, hwnd,
            Region: new(-2000, -1000, 1920, 1080));
        using var stream = new MemoryStream();
        await ElementHintProtocol.WriteAsync(stream, request, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(request, await ElementHintProtocol.ReadAsync<HintRequest>(stream,
            ElementHintProtocol.MaxRequestBytes, CancellationToken.None));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2097153)]
    public async Task RejectLengthBeforeReadingOrAllocatingBody(int length) {
        using var stream = new MemoryStream(BitConverter.GetBytes(length));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ElementHintProtocol.ReadAsync<HintResponse>(stream, ElementHintProtocol.MaxResponseBytes, CancellationToken.None));
    }

    [Fact]
    public void RejectMismatchedVersionAndIdentityAndInvalidGeometry() {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Discover, Region: new(0, 0, 100, 100));
        var response = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.NoTargets, []);
        ElementHintProtocol.CheckResponse(request, response);
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with { Version = 2 }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with { RequestId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Targets = [new(1, [1], 1, 50000, HintCapabilities.Invoke, new(double.NaN, 0, 10, 10), new(0, 0, 10, 10), new(5, 5))],
        }));
    }

    [Fact]
    public async Task ResponseByteCapRetainsAnExplicitPartialSnapshot() {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Discover,
            Region: new(-2000000000, -2000000000, 2000000001, 2000000001));
        var bounds = new HintRect(-1999999999.1234567, -1999999999.1234567, 1999999999.2345679, 1999999999.2345679);
        var targets = Enumerable.Range(1, ElementHintProtocol.MaxTargets).Select(i => new HintTarget(i,
            Enumerable.Repeat(int.MinValue, ElementHintProtocol.MaxRuntimeId).ToArray(), int.MaxValue, 50000,
            HintCapabilities.Invoke, bounds, bounds, new(-999999999, -999999999))).ToArray();
        var response = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.Success,
            targets, Visited: ElementHintProtocol.MaxNodes, RootProcessId: 1);
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ElementHintProtocol.WriteAsync(stream, response,
            ElementHintProtocol.MaxResponseBytes, CancellationToken.None));
        var bounded = ElementHintProtocol.BoundSnapshotResponse(response);
        Assert.Equal(HintOutcome.Partial, bounded.Outcome);
        Assert.NotEmpty(bounded.Targets);
        Assert.True(bounded.Targets.Length < targets.Length);
        Assert.Equal(targets.Length - bounded.Targets.Length, bounded.Omitted);
        Assert.Equal("Response byte limit", bounded.Reason);
        ElementHintProtocol.CheckResponse(request, bounded);
        await ElementHintProtocol.WriteAsync(stream, bounded, ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        Assert.True(stream.Length <= ElementHintProtocol.MaxResponseBytes + 4);
        Assert.Same(bounded, ElementHintProtocol.BoundSnapshotResponse(bounded));
    }
}

public class ElementHintCandidatePolicyTests {
    [Theory]
    [InlineData(50000)]
    [InlineData(50004)]
    [InlineData(50005)]
    [InlineData(50002)]
    [InlineData(50003)]
    [InlineData(50007)]
    [InlineData(50019)]
    [InlineData(50029)]
    public void StandardInteractiveTypesAreTargets(int type) =>
        Assert.True(ElementHintCandidatePolicy.IsInteractive(type, HintCapabilities.None));
    [Fact]
    public void PassiveTextAndFocusableContainersAreNotTargetsButCustomPatternsAre() {
        Assert.False(ElementHintCandidatePolicy.IsInteractive(50020, HintCapabilities.None));
        Assert.False(ElementHintCandidatePolicy.IsInteractive(50026, HintCapabilities.None));
        Assert.True(ElementHintCandidatePolicy.IsInteractive(50025, HintCapabilities.Toggle));
    }
}

public class ElementHintGeometryTests {
    [Fact]
    public void NegativeOriginsClipWithoutMovingPhysicalPointToLabels() {
        var bounds = new HintRect(-2000, -300, 200, 100);
        var clip = bounds.Clip(new(-1920, -1080, 1920, 1080));
        Assert.Equal(new HintRect(-1920, -300, 120, 100), clip);
        Assert.True(clip.Contains(clip.Center));
    }
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidCoordinatesCannotProduceGeometry(double x) =>
        Assert.False(new HintRect(x, 0, 10, 10).IsValid);
}
