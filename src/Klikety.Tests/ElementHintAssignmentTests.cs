using System.Drawing;

using Klikety.Automation;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public sealed class ElementHintAssignmentTests {
    private static HintEntry Entry(int id) {
        var target = FakeElementHintService.Result(1).Targets[0] with { Token = id, RuntimeId = [42, id] };
        return new(id, target, target.Bounds, target.Preview, "", []);
    }
    private static string Layout(Rectangle? bounds = null, int capacity = 4, VKey[]? horizontal = null) =>
        ElementHintAssignments.Layout(bounds ?? new(0, 0, 100, 100), capacity, 2,
            horizontal ?? [VKey.A, VKey.S], [VKey.Q, VKey.W]);
    private static HintAssignmentSnapshot Snapshot(params int[] ids) {
        var scan = new ElementHintAssignments(null);
        var level = scan.Level("root", false, 2, 4);
        foreach (int id in ids) {
            var entry = Entry(id);
            level.Allocate(entry, scan.EntryKey(entry));
        }
        return scan.Capture(Layout(), out _);
    }

    [Fact]
    public void ExactAssignmentsReserveHolesAndRestoreCurrentTokensInAnyOrder() {
        var scan = new ElementHintAssignments(Snapshot(1, 2, 3));
        var level = scan.Level("root", true, 2, 4);
        var existing = Entry(3) with { Id = 30, Target = Entry(3).Target! with { Token = 30 } };
        level.Allocate(existing, scan.EntryKey(existing));
        level.Allocate(Entry(4), scan.EntryKey(Entry(4)));
        Assert.False(level.SingleKey);
        Assert.Equal(2, level.Slot(30));
        Assert.Equal(3, level.Slot(4));
        var saved = scan.Capture(Layout(), out bool limited);
        Assert.False(limited);
        Assert.Equal(2, saved.Levels["root"].Slots.Count);
        var next = new ElementHintAssignments(saved);
        var nextLevel = next.Level("root", false, 2, 4);
        nextLevel.Allocate(Entry(5), next.EntryKey(Entry(5)));
        Assert.Equal(0, nextLevel.Slot(5));
    }

    [Theory]
    [InlineData(50001, HintCapabilities.Invoke)]
    [InlineData(50000, HintCapabilities.Toggle)]
    public void ChangedRoleOrCapabilitiesDoNotBorrowOldSlots(int type, HintCapabilities capabilities) {
        var scan = new ElementHintAssignments(Snapshot(1));
        var entry = Entry(1) with {
            Id = 10,
            Target = Entry(1).Target! with {
                Token = 10,
                ControlType = type,
                Capabilities = capabilities
            }
        };
        var level = scan.Level("root", false, 2, 4);
        level.Allocate(entry, scan.EntryKey(entry));
        Assert.Equal(1, level.Slot(10));
    }

    [Fact]
    public void GroupKeysIgnoreGeneratedIdsAndOrderingButTrackMembersNestingAndParent() {
        var scan = new ElementHintAssignments(null);
        HintEntry Group(int id, params HintEntry[] children) => new(id, null, new(0, 0, 100, 100), new(50, 50), "", children);
        var first = Group(-1, Entry(1), Entry(2));
        var reordered = Group(-2, Entry(2), Entry(1));
        Assert.Equal(scan.EntryKey(first), scan.EntryKey(reordered));
        Assert.NotEqual(scan.EntryKey(first), scan.EntryKey(Group(-3, Entry(1), Entry(3))));
        Assert.NotEqual(scan.EntryKey(first), scan.EntryKey(Group(-4, Group(-5, Entry(1)), Entry(2))));
        Assert.NotEqual(scan.ChildLevelKey("parent1", first), scan.ChildLevelKey("parent2", reordered));
        Assert.Null(scan.EntryKey(Group(-6) with { RemoteGroupId = 1 }));
    }

    [Fact]
    public void LruIdentityLayoutAndFactoryRetirementBoundLifetime() {
        var cache = new ElementHintAssignmentCache(2);
        ElementTargetContext Context(int hwnd, long start = 1) => new(hwnd, 1, 42, start);
        cache.Save(Context(1), Snapshot(1));
        cache.Save(Context(2), Snapshot(2));
        Assert.NotNull(cache.Load(Context(1), Layout()));
        cache.Save(Context(3), Snapshot(3));
        Assert.Null(cache.Load(Context(2), Layout()));
        Assert.Equal(2, cache.Count);
        Assert.Null(cache.Load(Context(1, 2), Layout()));
        cache.Save(Context(1, 2), Snapshot(4));
        Assert.Null(cache.Load(Context(1), Layout()));
        Assert.Null(cache.Load(Context(1, 2), Layout(new(0, 0, 101, 100))));
        Assert.Null(cache.Load(Context(1, 2), Layout(capacity: 3)));
        Assert.Null(cache.Load(Context(1, 2), Layout(horizontal: [VKey.S, VKey.A])));
        cache.Retire();
        cache.Save(Context(1), Snapshot(1));
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Load(Context(1), Layout()));
    }

    [Theory]
    [InlineData(0, 1, 42, 1)]
    [InlineData(2, 0, 42, 1)]
    [InlineData(2, 1, 0, 1)]
    [InlineData(2, 1, 42, 0)]
    public void DisabledRetentionOrMissingIdentityDoesNotStore(int capacity, int hwnd, int pid, long start) {
        var cache = new ElementHintAssignmentCache(capacity);
        var context = new ElementTargetContext(hwnd, 1, pid, start);
        cache.Save(context, Snapshot(1));
        Assert.Equal(0, cache.Count);
        Assert.Null(cache.Load(context, Layout()));
    }

    [Fact]
    public void SnapshotIncludesOffPageAndVisitedLevelsAndCapsRecords() {
        var scan = new ElementHintAssignments(null);
        var root = scan.Level("root", false, 1, 1);
        for (int i = 1; i <= ElementHintProtocol.MaxTargets + 1; i++) {
            var entry = Entry(i);
            root.Allocate(entry, scan.EntryKey(entry));
        }
        var child = scan.Level("child", true, 2, 4);
        child.Allocate(Entry(3000), scan.EntryKey(Entry(3000)));
        var snapshot = scan.Capture(Layout(), out bool limited);
        Assert.True(limited);
        Assert.Equal(ElementHintProtocol.MaxTargets, snapshot.Levels.Sum(l => l.Value.Slots.Count));
        Assert.Equal(ElementHintProtocol.MaxTargets - 1, snapshot.Levels["root"].Slots.Values.Max());
        Assert.DoesNotContain("child", snapshot.Levels.Keys);
    }

    [Fact]
    public void GroupRecordLimitAndVisitedLevelSnapshotAreIndependentOfControlLimit() {
        var scan = new ElementHintAssignments(null);
        var root = scan.Level("root", false, 2, 4);
        for (int i = 1; i <= ElementHintProtocol.MaxContainers + 1; i++) {
            var group = new HintEntry(-i, null, new(0, 0, 10, 10), new(5, 5), "", [Entry(i)]);
            root.Allocate(group, scan.EntryKey(group));
        }
        var visited = scan.Level("visited", true, 2, 4);
        visited.Allocate(Entry(6000), scan.EntryKey(Entry(6000)));
        var snapshot = scan.Capture(Layout(), out bool limited);
        Assert.True(limited);
        Assert.Equal(ElementHintProtocol.MaxContainers, snapshot.Levels["root"].Slots.Count);
        Assert.Single(snapshot.Levels["visited"].Slots);
    }

    [Fact]
    public void MovingControlsDoesNotChangeIdentityAndSingleSchemeSurvivesGrowth() {
        var scan = new ElementHintAssignments(null);
        var first = scan.Level("root", true, 2, 4);
        var entry = Entry(1);
        first.Allocate(entry, scan.EntryKey(entry));
        var next = new ElementHintAssignments(scan.Capture(Layout(), out _));
        var grown = next.Level("root", false, 2, 4);
        var moved = entry with { Target = entry.Target! with { Bounds = new(500, 500, 10, 10) } };
        grown.Allocate(moved, next.EntryKey(moved));
        foreach (int id in new[] { 2, 3 }) { grown.Allocate(Entry(id), next.EntryKey(Entry(id))); }
        Assert.True(grown.SingleKey);
        Assert.Equal(2, grown.Capacity);
        Assert.Equal(0, grown.Slot(1));
        Assert.Equal(2, grown.Slot(3));
    }
}
