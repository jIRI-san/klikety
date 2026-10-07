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
    public const double TargetWidthRatio = 0.75;
    private const double MinimumKeyUnit = 40;
    private const double KeyboardWidthInUnits = 17.16;

    public double TextScale => KeyUnit / MinimumKeyUnit;

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
        var width = Math.Max(800, viewportWidth);
        var height = Math.Max(600, viewportHeight);
        var supported = LeftRows.Concat(RightRows).SelectMany(row => row)
            .Concat(Enumerable.Range(0, 10).Select(index => (VKey)((int)VKey.F1 + index)))
            .Concat(Enumerable.Range(0, 10).Select(index => (VKey)((int)VKey.D0 + index)))
            .Append(VKey.Space)
            .ToHashSet();
        var auxiliaryKeys = commandKeys.Concat(navigationAnchors)
            .Distinct()
            .Where(key => !supported.Contains(key))
            .OrderBy(key => (int)key)
            .ToArray();
        const int auxiliaryColumns = 4;
        var auxiliaryRows = (auxiliaryKeys.Length + auxiliaryColumns - 1) / auxiliaryColumns;
        var footerHeight = 44 + Math.Max(0, extraFooterLines) * 18;
        var widthUnit = width * TargetWidthRatio / KeyboardWidthInUnits;
        var heightUnit = (height / 2 - 40 - footerHeight) / (3.85 + auxiliaryRows * 1.5);
        var unit = Math.Max(MinimumKeyUnit, Math.Min(widthUnit, heightUnit));
        var pitch = unit * 1.55;
        var rowPitch = unit * 1.5;
        var halfWidth = 4 * pitch + unit * (0.58 + 1.4);
        var middleGap = unit * 0.8;
        var center = width / 2;
        var left = center - middleGap / 2 - halfWidth;
        var right = center + middleGap / 2;
        var top = Math.Max(20, height / 2 + 40 - 3.5 * rowPitch);
        var positions = new Dictionary<VKey, HelpKeyPosition>();

        void Add(VKey key, double x, double y) {
            positions[key] = new HelpKeyPosition(
                key,
                x,
                y,
                unit * 1.4,
                unit * 1.5,
                false);
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

        positions[VKey.Space] = new HelpKeyPosition(
            VKey.Space,
            center - unit * 1.8,
            top + 5 * rowPitch,
            unit * 3.6,
            unit * 1.5,
            false);

        var auxiliaryTop = top + 6 * rowPitch + unit * 0.1;
        var auxiliaryWidth = unit * 4;
        var auxiliaryGap = unit * 0.2;
        for (var index = 0; index < auxiliaryKeys.Length; index++) {
            var column = index % auxiliaryColumns;
            var row = index / auxiliaryColumns;
            var rowColumns = Math.Min(auxiliaryColumns, auxiliaryKeys.Length - row * auxiliaryColumns);
            var rowWidth = rowColumns * auxiliaryWidth + (rowColumns - 1) * auxiliaryGap;
            positions[auxiliaryKeys[index]] = new HelpKeyPosition(
                auxiliaryKeys[index],
                center - rowWidth / 2 + column * (auxiliaryWidth + auxiliaryGap),
                auxiliaryTop + row * rowPitch,
                auxiliaryWidth,
                unit * 1.5,
                true);
        }

        var requiredHeight = auxiliaryTop + auxiliaryRows * rowPitch + footerHeight;
        return new HelpKeyboardLayout(
            width,
            Math.Max(height, requiredHeight),
            unit,
            viewportWidth < 800 || viewportHeight < 600 || requiredHeight > height,
            positions.Values.OrderBy(position => position.Y).ThenBy(position => position.X).ToArray());
    }
}
