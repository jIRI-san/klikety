namespace Klikety.Input;

/// <summary>
/// Win32 virtual-key codes. Values match MSDN VK_* constants.
/// Used throughout the app as the stable, layout-independent key identifier.
/// Display labels are derived separately via ToUnicode/MapVirtualKey.
/// </summary>
public enum VKey
{
    // Special / control
    Back      = 0x08,
    Tab       = 0x09,
    Return    = 0x0D,
    Shift     = 0x10,
    Control   = 0x11,
    Menu      = 0x12, // Alt
    Pause     = 0x13,
    Capital   = 0x14, // Caps Lock
    Escape    = 0x1B,
    Space     = 0x20,

    // Arrow keys (always reserved — may not appear in firstKeys/secondKeys/ActionBindings)
    Left      = 0x25,
    Up        = 0x26,
    Right     = 0x27,
    Down      = 0x28,

    // Modifier variants
    LShift    = 0xA0,
    RShift    = 0xA1,
    LControl  = 0xA2,
    RControl  = 0xA3,
    LMenu     = 0xA4, // Left Alt
    RMenu     = 0xA5, // Right Alt
    LWin      = 0x5B,
    RWin      = 0x5C,

    // Digits (top row)
    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,

    // Alphabet — stable physical positions regardless of layout
    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,

    // OEM keys (common punctuation — physical positions vary by layout)
    OemSemicolon  = 0xBA, // ;: on US QWERTY
    OemPlus       = 0xBB, // =+
    OemComma      = 0xBC, // ,<
    OemMinus      = 0xBD, // -_
    OemPeriod     = 0xBE, // .>
    OemQuestion   = 0xBF, // /?
    OemTilde      = 0xC0, // `~
    OemOpenBrackets  = 0xDB, // [{
    OemPipe          = 0xDC, // \|
    OemCloseBrackets = 0xDD, // ]}
    OemQuotes        = 0xDE, // '"
}
