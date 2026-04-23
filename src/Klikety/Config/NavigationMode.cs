namespace Klikety.Config;

/// <summary>
/// Controls which navigation input schemes are active.
/// </summary>
public enum NavigationMode
{
    /// <summary>Two-key grid scheme only. Arrow keys and Enter are ignored.</summary>
    TwoKey,

    /// <summary>Arrow key navigation only. First/second key pairs are ignored.</summary>
    Arrow,

    /// <summary>Both schemes active simultaneously (default).</summary>
    Both,
}
