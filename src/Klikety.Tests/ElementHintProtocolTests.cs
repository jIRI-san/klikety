using System.IO;

using Klikety.Automation;

namespace Klikety.Tests;

public class ElementHintProtocolTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitMoveOnlyIntentRoundTripsWithoutChangingOtherRequestFields(bool moveOnly) {
        var request = new HintRequest(ElementHintProtocol.Version, Guid.NewGuid(), Guid.NewGuid(),
            HintCommand.Validate, Token: 3, MoveOnly: moveOnly);
        using var stream = new MemoryStream();
        await ElementHintProtocol.WriteAsync(stream, request, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(request, await ElementHintProtocol.ReadAsync<HintRequest>(stream,
            ElementHintProtocol.MaxRequestBytes, CancellationToken.None));
    }

    [Fact]
    public async Task MissingMoveOnlyFieldKeepsStrictValidationForOlderRequests() {
        byte[] body = System.Text.Encoding.UTF8.GetBytes("""{"Version":1,"Command":2}""");
        using var stream = new MemoryStream([.. BitConverter.GetBytes(body.Length), .. body]);
        var request = await ElementHintProtocol.ReadAsync<HintRequest>(stream,
            ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        Assert.Equal(HintCommand.Validate, request.Command);
        Assert.False(request.MoveOnly);
        Assert.Equal(5, request.CacheWindowCount);
    }

    [Fact]
    public async Task OptionalGroupMetadataRoundTripsAndRejectsEmptyChildCounts() {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Expand,
            Region: new(0, 0, 100, 100), Incremental: true, GroupId: 2);
        using var stream = new MemoryStream();
        await ElementHintProtocol.WriteAsync(stream, request, ElementHintProtocol.MaxRequestBytes, CancellationToken.None);
        stream.Position = 0;
        Assert.Equal(request, await ElementHintProtocol.ReadAsync<HintRequest>(stream,
            ElementHintProtocol.MaxRequestBytes, CancellationToken.None));
        var response = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.Success, [],
            RootProcessId: 42, GroupId: 2, Groups: [new(2, 0, new(0, 0, 100, 100), 41), new(3, 2, new(0, 0, 100, 100), 8)]);
        ElementHintProtocol.CheckResponse(request, response);
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Groups = [response.Groups![0] with { ChildCount = 0 }, response.Groups[1]]
        }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Groups = [response.Groups![0], response.Groups[1] with { ChildCount = 0 }]
        }));
    }

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
            targets.Select(t => t with { ContainerId = 1 }).ToArray(),
            Visited: ElementHintProtocol.MaxNodes, RootProcessId: 1,
            Containers: [new(1, 0, 1, int.MaxValue, 50000, bounds)]);
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => ElementHintProtocol.WriteAsync(stream, response,
            ElementHintProtocol.MaxResponseBytes, CancellationToken.None));
        var bounded = ElementHintProtocol.BoundSnapshotResponse(response);
        Assert.Equal(HintOutcome.Partial, bounded.Outcome);
        Assert.NotEmpty(bounded.Targets);
        Assert.True(bounded.Targets.Length < targets.Length);
        Assert.Equal(targets.Length - bounded.Targets.Length, bounded.Omitted);
        Assert.Equal("Response byte limit", bounded.Reason);
        Assert.Null(bounded.Containers);
        Assert.All(bounded.Targets, t => Assert.Equal(0, t.ContainerId));
        ElementHintProtocol.CheckResponse(request, bounded);
        await ElementHintProtocol.WriteAsync(stream, bounded, ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
        Assert.True(stream.Length <= ElementHintProtocol.MaxResponseBytes + 4);
        Assert.Same(bounded, ElementHintProtocol.BoundSnapshotResponse(bounded));
    }

    [Fact]
    public async Task ProgressiveByteCapDoesNotRemovePublishedTargetsWhenNewGroupsConsumeTheBudget() {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Continue,
            Region: new(-2000000000, -2000000000, 2000000001, 2000000001), Incremental: true);
        var bounds = new HintRect(-1999999999.1234567, -1999999999.1234567, 1999999999.2345679, 1999999999.2345679);
        var targets = Enumerable.Range(1, ElementHintProtocol.MaxTargets).Select(i => new HintTarget(i,
            Enumerable.Repeat(int.MinValue, ElementHintProtocol.MaxRuntimeId).ToArray(), int.MaxValue, 50000,
            HintCapabilities.Invoke, bounds, bounds, new(-999999999, -999999999))).ToArray();
        var full = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.Success, targets,
            RootProcessId: 1, IsComplete: false);
        var published = ElementHintProtocol.BoundSnapshotResponse(full) with {
            Outcome = HintOutcome.Success,
            Reason = null,
            Omitted = 0,
            IsComplete = false
        };
        ElementHintProtocol.CheckResponse(request, published);
        var current = published with {
            Groups = Enumerable.Range(1, ElementHintProtocol.MaxTargets)
                .Select(i => new HintGroup(i, 0, bounds, 31)).ToArray()
        };
        var bounded = ElementHintProtocol.BoundSnapshotResponse(current, published);
        Assert.Equal(HintOutcome.Partial, bounded.Outcome);
        Assert.True(bounded.IsComplete);
        Assert.Equal(published.Targets, bounded.Targets);
        Assert.Equal(ElementHintProtocol.MaxTargets, bounded.Omitted);
        Assert.Equal("Response byte limit", bounded.Reason);
        ElementHintProtocol.CheckProgress(published, bounded);
        ElementHintProtocol.CheckResponse(request, bounded);
        using var stream = new MemoryStream();
        await ElementHintProtocol.WriteAsync(stream, bounded, ElementHintProtocol.MaxResponseBytes, CancellationToken.None);
    }

    [Fact]
    public void ProgressFramesRequireKnownGroupsValidScopeAndAUsableRoot() {
        var request = new HintRequest(1, Guid.NewGuid(), Guid.NewGuid(), HintCommand.Continue,
            Region: new(0, 0, 100, 100), Incremental: true);
        var response = new HintResponse(1, request.SessionId, request.RequestId, HintOutcome.Success, [],
            RootProcessId: 42, Groups: [new(2, 0, new(0, 0, 100, 100), 11)], IsComplete: false);
        ElementHintProtocol.CheckResponse(request, response);
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with { RootProcessId = 0 }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Groups = [new(2, 3, new(0, 0, 100, 100), 11)]
        }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Groups = [new(2, 0, new(0, 0, 100, 100), 0)]
        }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with { GroupId = 2 }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckResponse(request, response with {
            Targets = [new(1, [42, 1], 42, 50000, HintCapabilities.Invoke,
                new(0, 0, 10, 10), new(0, 0, 10, 10), new(5, 5), DiscoveryGroupId: 3)]
        }));
    }

    [Fact]
    public void ProgressMayAppendButCannotRemoveTargetsOrChangeGroupIdentity() {
        var first = FakeElementHintService.Result(1) with {
            Groups = [new(2, 0, new(10, 10, 100, 100), 11)],
            IsComplete = false
        };
        var next = FakeElementHintService.Result(2) with { Groups = first.Groups };
        ElementHintProtocol.CheckProgress(first, next);
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckProgress(first, next with { Targets = [next.Targets[1]] }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckProgress(first, next with {
            Groups = [first.Groups![0] with { Bounds = new(20, 10, 100, 100) }]
        }));
        Assert.Throws<InvalidDataException>(() => ElementHintProtocol.CheckProgress(first, next with { RootProcessId = 99 }));
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
