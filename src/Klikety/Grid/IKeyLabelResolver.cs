using Klikety.Input;

namespace Klikety.Grid;

/// <summary>
/// Resolves a VKey to its display character using the active keyboard layout.
/// </summary>
public interface IKeyLabelResolver {
    /// <summary>
    /// Returns the uppercase display character for <paramref name="key"/>,
    /// or the VKey name if the key has no printable character.
    /// </summary>
    string Resolve(VKey key);
}
