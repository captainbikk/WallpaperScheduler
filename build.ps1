$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Add-Type -AssemblyName System.Drawing

$iconSource = @"
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconMaker {
    public static void Save(string path) {
        byte[] a = Png(16);
        byte[] b = Png(32);
        byte[] c = Png(48);
        byte[] d = Png(256);
        byte[][] images = new byte[][] { a, b, c, d };
        int[] sizes = new int[] { 16, 32, 48, 256 };
        using (var fs = File.Create(path))
        using (var w = new BinaryWriter(fs)) {
            w.Write((ushort)0);
            w.Write((ushort)1);
            w.Write((ushort)images.Length);
            int offset = 6 + (16 * images.Length);
            for (int i = 0; i < images.Length; i++) {
                int size = sizes[i] >= 256 ? 0 : sizes[i];
                w.Write((byte)size);
                w.Write((byte)size);
                w.Write((byte)0);
                w.Write((byte)0);
                w.Write((ushort)1);
                w.Write((ushort)32);
                w.Write(images[i].Length);
                w.Write(offset);
                offset += images[i].Length;
            }
            for (int i = 0; i < images.Length; i++) w.Write(images[i]);
        }
    }

    static byte[] Png(int size) {
        using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(bmp)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float u = size / 32f;
            using (var blue = new SolidBrush(Color.FromArgb(47, 111, 237)))
                FillRound(g, blue, 1 * u, 1 * u, 30 * u, 30 * u, 6 * u);
            using (var page = new SolidBrush(Color.White))
                FillRound(g, page, 6 * u, 8 * u, 20 * u, 16 * u, 2 * u);
            using (var bar = new SolidBrush(Color.FromArgb(22, 50, 79)))
                g.FillRectangle(bar, 6 * u, 8 * u, 20 * u, 4.2f * u);
            using (var sun = new SolidBrush(Color.FromArgb(255, 186, 64)))
                g.FillEllipse(sun, 17.5f * u, 13.2f * u, 5.2f * u, 5.2f * u);
            using (var hill = new SolidBrush(Color.FromArgb(72, 166, 104)))
                g.FillEllipse(hill, 8 * u, 18.5f * u, 16 * u, 8 * u);
            using (var ms = new MemoryStream()) {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }
    }

    static void FillRound(Graphics g, Brush brush, float x, float y, float w, float h, float r) {
        if (r < 1f) r = 1f;
        using (var path = new GraphicsPath()) {
            float d = r * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }
}
"@

Add-Type -TypeDefinition $iconSource -ReferencedAssemblies System.Drawing
[IconMaker]::Save((Join-Path $root "app.ico"))

dotnet build (Join-Path $root "WallpaperScheduler.csproj") -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outDir = Join-Path $root "bin\Release\net48"
Copy-Item (Join-Path $outDir "WallpaperScheduler.exe") (Join-Path $root "WallpaperScheduler.exe") -Force
$config = Join-Path $outDir "WallpaperScheduler.exe.config"
if (Test-Path $config) {
    Copy-Item $config (Join-Path $root "WallpaperScheduler.exe.config") -Force
}

function New-Shortcut($path, $target) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($path)
    $link.TargetPath = $target
    $link.WorkingDirectory = $root
    $link.Description = "Change a monitor wallpaper on a date, for as long as you choose."
    $link.IconLocation = "$target,0"
    $link.Save()
}

$exe = Join-Path $root "WallpaperScheduler.exe"
$desktop = [Environment]::GetFolderPath("Desktop")
$start = Join-Path ([Environment]::GetFolderPath("Programs")) "Wallpaper Scheduler.lnk"
New-Shortcut (Join-Path $desktop "Wallpaper Scheduler.lnk") $exe
New-Shortcut $start $exe
Write-Output "Built $exe"
