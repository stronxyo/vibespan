// Renders the Claude mark at a large size so the geometry can be eyeballed, and proves
// Geometry.Parse accepts the path at runtime (compiling only proves the string is a string).
using System;
using System.IO;
using System.Windows.Media;
using Vibespan;

public static class TestGlyph
{
    [STAThread]
    public static void Main()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xDA, 0x77, 0x56));
        brush.Freeze();
        foreach (int px in new[] { 256, 64, 16 })
        {
            byte[] png = Gauge.MarkPng(px, brush);
            string outPath = Path.Combine("docs", "glyph-" + px + ".png");
            File.WriteAllBytes(outPath, png);
            Console.WriteLine("wrote " + outPath + "  (" + png.Length + " bytes)");
        }
        Console.WriteLine("Geometry.Parse accepted the path at runtime.");
    }
}
