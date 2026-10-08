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
$files = Get-ChildItem -LiteralPath $CaptureDirectory -Filter 'frame-*.png' | Sort-Object Name
if ($files.Count -lt 2) { throw 'At least two captured navigation frames are required.' }
$frames = New-Object 'Collections.Generic.List[byte[]]'
foreach ($file in $files) {
    $image = New-Object Windows.Media.Imaging.BitmapImage
    $image.BeginInit()
    $image.CacheOption = [Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $image.UriSource = New-Object Uri($file.FullName)
    $image.DecodePixelWidth = 960
    $image.EndInit()
    $rgba = New-Object Windows.Media.Imaging.FormatConvertedBitmap(
        $image, [Windows.Media.PixelFormats]::Bgra32, $null, 0)
    $encoder = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($rgba))
    $stream = New-Object IO.MemoryStream
    try { $encoder.Save($stream); $frames.Add($stream.ToArray()) } finally { $stream.Dispose() }
}
[DemoApng]::Encode($frames.ToArray(), [IO.Path]::GetFullPath($OutputFile))
