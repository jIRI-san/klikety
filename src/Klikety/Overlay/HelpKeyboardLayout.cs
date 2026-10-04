using Klikety.Input;

namespace Klikety.Overlay;

public sealed record HelpKeyPosition(VKey Key, double X, double Y, double Width, double Height, bool IsAuxiliary);

public sealed record HelpKeyboardLayout(
    double ContentWidth,
    double ContentHeight,
    double KeyUnit,
    bool ScrollViewport,
    IReadOnlyList<HelpKeyPosition> Positions) {
    public const double MinimumCommandFontSize = 12;

    private static readonly VKey[][] LeftRows = [
        [VKey.Q, VKey.W, VKey.E, VKey.R, VKey.T],
        [VKey.A, VKey.S, VKey.D, VKey.F, VKey.G],
        [VKey.Z, VKey.X, VKey.C, VKey.V, VKey.B],
    ];

    private static readonly VKey[][] RightRows = [
        [VKey.Y, VKey.U, VKey.I, VKey.O, VKey.P],
        [VKey.H, VKey.J, VKey.K, VKey.L, VKey.OemSemicolon],
        [VKey.N, VKey.M, VKey.OemComma, VKey.OemPeriod, VKey.OemQuestion],
    ];

    public static HelpKeyboardLayout Compute(
        double viewportWidth,
        double viewportHeight,
        IEnumerable<VKey> commandKeys,
        IEnumerable<VKey> navigationAnchors,
        int extraFooterLines = 0) {
        var width = Math.Max(0, viewportWidth);
        var height = Math.Max(0, viewportHeight);
        var unit = Math.Clamp(Math.Min((width - 80) / 16.0, (height - 230) / 9.0), 32, 40);
        var pitch = unit * 1.55;
        var rowPitch = unit * 1.5;
        var halfWidth = 5 * pitch;
        var middleGap = unit * 0.8;
        var center = width / 2;
        var left = center - middleGap / 2 - halfWidth;
        var right = center + middleGap / 2;
        var top = Math.Max(20, height / 2 - 170);
        var positions = new Dictionary<VKey, HelpKeyPosition>();

        void Add(VKey key, double x, double y, bool auxiliary = false) {
            positions[key] = new HelpKeyPosition(
                key,
                x,
                y,
                auxiliary ? unit * 4 : unit * 1.4,
                unit * 1.5,
                auxiliary);
        }

        for (var index = 0; index < 10; index++) {
            var half = index < 5 ? left : right;
            var column = index % 5;
            Add((VKey)((int)VKey.F1 + index), half + column * pitch, top);
            var digit = index < 5 ? index + 1 : index == 9 ? 0 : index + 1;
            Add((VKey)((int)VKey.D0 + digit), half + column * pitch, top + rowPitch);
        }

        for (var row = 0; row < LeftRows.Length; row++) {
            var y = top + (row + 2) * rowPitch;
            var stagger = row == 0 ? 0 : row == 1 ? unit * 0.22 : unit * 0.58;
            for (var column = 0; column < LeftRows[row].Length; column++) {
                Add(LeftRows[row][column], left + stagger + column * pitch, y);
                Add(RightRows[row][column], right + stagger + column * pitch, y);
            }
        }

        Add(VKey.Space, center - unit * 1.8, top + 5 * rowPitch, false);

        var supported = positions.Keys.ToHashSet();
        var auxiliaryKeys = commandKeys.Concat(navigationAnchors)
            .Distinct()
            .Where(key => !supported.Contains(key))
            .OrderBy(key => (int)key)
            .ToArray();
        var auxiliaryTop = top + 6 * rowPitch + unit * 0.1;
        var auxiliaryColumns = Math.Max(1, (int)(Math.Max(width - 40, 200) / (unit * 4.2)));
        var auxiliaryWidth = Math.Max(unit * 4.1, (width - 40) / auxiliaryColumns);
        for (var index = 0; index < auxiliaryKeys.Length; index++) {
            var column = index % auxiliaryColumns;
            var row = index / auxiliaryColumns;
            positions[auxiliaryKeys[index]] = new HelpKeyPosition(
                auxiliaryKeys[index],
                20 + column * auxiliaryWidth,
                auxiliaryTop + row * rowPitch,
                auxiliaryWidth - unit * 0.1,
                unit * 1.5,
                true);
        }

        var auxiliaryRows = auxiliaryKeys.Length == 0
            ? 0
            : (auxiliaryKeys.Length + auxiliaryColumns - 1) / auxiliaryColumns;
        var requiredHeight = auxiliaryTop + auxiliaryRows * rowPitch + 44 + Math.Max(0, extraFooterLines) * 18;
        return new HelpKeyboardLayout(
            Math.Max(width, 20 + auxiliaryColumns * auxiliaryWidth),
            Math.Max(height, requiredHeight),
            unit,
            width < 800 || height < 600 || requiredHeight > height || width < 600,
            positions.Values.OrderBy(position => position.Y).ThenBy(position => position.X).ToArray());
    }
}
