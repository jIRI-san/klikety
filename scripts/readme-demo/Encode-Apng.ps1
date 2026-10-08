param(
    [Parameter(Mandatory = $true)][string]$CaptureDirectory,
    [Parameter(Mandatory = $true)][string]$OutputFile
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
Add-Type @'
using System;
using System.IO;
using System.Linq;
using System.Text;
public static class DemoApng {
    static uint Read(byte[] data, int offset) {
        return ((uint)data[offset] << 24) | ((uint)data[offset+1] << 16) |
            ((uint)data[offset+2] << 8) | data[offset+3];
    }
    static void Write(byte[] data, int offset, uint value) {
        for (int i = 0; i < 4; i++) data[offset+i] = (byte)(value >> (24-i*8));
    }
    static void Chunk(Stream output, string type, byte[] body) {
        byte[] data = Encoding.ASCII.GetBytes(type).Concat(body).ToArray();
        uint crc = uint.MaxValue;
        foreach (byte value in data) {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xEDB88320u);
        }
        byte[] length = new byte[4], checksum = new byte[4];
        Write(length, 0, (uint)body.Length); Write(checksum, 0, ~crc);
        output.Write(length, 0, 4); output.Write(data, 0, data.Length); output.Write(checksum, 0, 4);
    }
    public static void Encode(byte[][] frames, string path) {
        if (frames.Length < 2) throw new ArgumentException("At least two frames are required.");
        byte[] header = frames[0].Skip(16).Take(13).ToArray();
        if (header.Length != 13 || header[9] != 6) throw new InvalidDataException("Expected RGBA PNG frames.");
        using (var output = File.Create(path)) {
            output.Write(frames[0], 0, 8); Chunk(output, "IHDR", header);
            byte[] animation = new byte[8]; Write(animation, 0, (uint)frames.Length);
            Chunk(output, "acTL", animation);
            uint sequence = 0;
            for (int index = 0; index < frames.Length; index++) {
                byte[] png = frames[index];
                if (!header.SequenceEqual(png.Skip(16).Take(13)))
                    throw new InvalidDataException("Animation frame dimensions or format differ.");
                byte[] control = new byte[26]; Write(control, 0, sequence++);
                Write(control, 4, Read(header, 0)); Write(control, 8, Read(header, 4));
                control[21] = 14; control[23] = 10;
                Chunk(output, "fcTL", control);
                for (int offset = 8; offset < png.Length; ) {
                    int length = checked((int)Read(png, offset));
                    string type = Encoding.ASCII.GetString(png, offset+4, 4);
                    if (type == "IDAT") {
                        byte[] body = png.Skip(offset+8).Take(length).ToArray();
                        if (index == 0) Chunk(output, "IDAT", body);
                        else {
                            byte[] sequenceBytes = new byte[4]; Write(sequenceBytes, 0, sequence++);
                            Chunk(output, "fdAT", sequenceBytes.Concat(body).ToArray());
                        }
                    }
                    offset = checked(offset+length+12);
                }
            }
            Chunk(output, "IEND", new byte[0]);
        }
    }
}
'@
$plan = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AnimationFrames.json') -Raw | ConvertFrom-Json
$frameDirectory = Join-Path $CaptureDirectory 'animation-frames'
[void](New-Item -ItemType Directory -Path $frameDirectory -Force)
$frames = New-Object 'Collections.Generic.List[byte[]]'
foreach ($frame in $plan) {
    $image = New-Object Windows.Media.Imaging.BitmapImage
    $image.BeginInit()
    $image.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $image.UriSource = New-Object Uri([IO.Path]::GetFullPath((Join-Path $CaptureDirectory $frame.source)))
    $image.DecodePixelWidth = 960
    $image.EndInit()
    $visual = New-Object Windows.Media.DrawingVisual
    $drawing = $visual.RenderOpen()
    try {
        $drawing.DrawRectangle([Windows.Media.Brushes]::Black, $null,
            (New-Object Windows.Rect(0, 0, 960, ($image.PixelHeight + 40))))
        $drawing.DrawImage($image, (New-Object Windows.Rect(0, 40, 960, $image.PixelHeight)))
        $text = New-Object Windows.Media.FormattedText(
            $frame.caption, [Globalization.CultureInfo]::InvariantCulture, [Windows.FlowDirection]::LeftToRight,
            (New-Object Windows.Media.Typeface('Segoe UI')), 18, [Windows.Media.Brushes]::White, 1)
        if ($text.WidthIncludingTrailingWhitespace -gt 936 -or $text.Height -gt 32) {
            throw "Animation caption does not fit: $($frame.caption)"
        }
        $drawing.DrawText($text, (New-Object Windows.Point(12, ((40 - $text.Height) / 2))))
    } finally { $drawing.Close() }
    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap(
        960, ($image.PixelHeight + 40), 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $rgba = New-Object Windows.Media.Imaging.FormatConvertedBitmap(
        $bitmap, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rgba))
    $stream = New-Object IO.MemoryStream
    try {
        $encoder.Save($stream)
        $bytes = $stream.ToArray()
        [IO.File]::WriteAllBytes((Join-Path $frameDirectory ('animation-frame-{0:D2}.png' -f ($frames.Count + 1))), $bytes)
        $frames.Add($bytes)
    } finally { $stream.Dispose() }
}
[DemoApng]::Encode($frames.ToArray(), [IO.Path]::GetFullPath($OutputFile))
