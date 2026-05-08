using Klikety.Config;
using Klikety.Input;
using Klikety.Navigation;

namespace Klikety.Tests;

public class ActionMapperTests {
    [Fact]
    public void MoveOnly_MapsFromConfig() {
        var bindings = new Dictionary<string, MouseAction> { { "B", MouseAction.MoveOnly } };
        var mapper = new ActionMapper(bindings);

        Assert.Equal(MouseAction.MoveOnly, mapper.Map(VKey.B));
    }
}
