using System.Text.Json.Nodes;

using Klikety.Config;

namespace Klikety.Tests;

public sealed class SettingsDraftTests {
    [Fact]
    public void Set_TracksTypedChangeAndClearsWhenReverted() {
        var draft = new SettingsDraft(new ConfigModel());

        draft.Set("hotKey.key", JsonValue.Create("A"));

        Assert.True(draft.IsDirty);
        Assert.Equal("A", draft.Changes["hotKey.key"]!.GetValue<string>());

        draft.Set("hotKey.key", JsonValue.Create("Space"));

        Assert.False(draft.IsDirty);
        Assert.Empty(draft.Changes);
    }

    [Fact]
    public void Set_AllowsNullForNullableDraftFields() {
        var draft = new SettingsDraft(new ConfigModel());

        draft.Set("appScope.chordKey", null);

        Assert.True(draft.IsDirty);
        Assert.Null(draft.Changes["appScope.chordKey"]);
    }

    [Fact]
    public void Rebase_ClearsChangesAndUnknownFieldsAreRejected() {
        var draft = new SettingsDraft(new ConfigModel());
        draft.Set("hotKey.key", JsonValue.Create("A"));
        draft.Rebase(new ConfigModel());

        Assert.False(draft.IsDirty);
        Assert.Throws<InvalidDataException>(() => draft.Set("unknown.value", JsonValue.Create(1)));
    }
}
