using Klikety.Config;
using Klikety.Input;

namespace Klikety.Navigation;

/// <summary>
/// Maps pressed VKey to MouseAction using config ActionBindings.
/// Space always maps to LeftClick if unbound.
/// </summary>
public sealed class ActionMapper
{
    private readonly Dictionary<VKey, MouseAction> _bindings;

    public ActionMapper(Dictionary<string, MouseAction> configBindings)
    {
        _bindings = new Dictionary<VKey, MouseAction>();
        foreach (var (keyName, action) in configBindings)
        {
            if (Enum.TryParse<VKey>(keyName, true, out var vkey))
                _bindings[vkey] = action;
        }

        // Space → LeftClick is always the default if not explicitly bound
        _bindings.TryAdd(VKey.Space, MouseAction.LeftClick);
    }

    /// <summary>
    /// Returns the MouseAction for the pressed VKey, or null if not an action key.
    /// </summary>
    public MouseAction? Map(VKey vkey)
    {
        return _bindings.TryGetValue(vkey, out var action) ? action : null;
    }

    /// <summary>
    /// Returns true if the given VKey is bound as an action key.
    /// </summary>
    public bool IsActionKey(VKey vkey) => _bindings.ContainsKey(vkey);
}
