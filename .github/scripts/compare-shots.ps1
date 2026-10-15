# Compares this build's --shot screenshots with the base branch's, so a pull request shows which windows changed.
# Writes a table to the job summary and, for each changed window, a picture with the changed pixels in red.
# Informational only: it never fails the build (UI changes are often the point of a pull request).
# Runs in Windows PowerShell 5.1, whose .NET Framework has System.Drawing built in.
param([string]$Head = 'shots', [string]$Base = 'baseshots', [string]$Out = 'shots-diff')

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ShotDiff
{
    // Returns the share of pixels that differ (any channel by more than 24) and writes a highlight image.
    public static double Compare(string a, string b, string diffPath)
    {
        using (var x = new Bitmap(a))
        using (var y = new Bitmap(b))
        {
            if (x.Width != y.Width || x.Height != y.Height) return -1;
            var r = new Rectangle(0, 0, x.Width, x.Height);
            var dx = x.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var dy = y.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var n = dx.Stride * x.Height;
            var px = new byte[n]; var py = new byte[n];
            Marshal.Copy(dx.Scan0, px, 0, n); Marshal.Copy(dy.Scan0, py, 0, n);
            x.UnlockBits(dx); y.UnlockBits(dy);
            var outp = new byte[n];
            long changed = 0;
            for (var i = 0; i < n; i += 4)
            {
                var d = Math.Max(Math.Abs(px[i] - py[i]), Math.Max(Math.Abs(px[i + 1] - py[i + 1]), Math.Abs(px[i + 2] - py[i + 2])));
                if (d > 24) { changed++; outp[i] = 0; outp[i + 1] = 0; outp[i + 2] = 255; }
                else { var g = (byte)((px[i] + px[i + 1] + px[i + 2]) / 9 + 40); outp[i] = g; outp[i + 1] = g; outp[i + 2] = g; }
                outp[i + 3] = 255;
            }
            if (changed > 0)
            {
                using (var o = new Bitmap(x.Width, x.Height, PixelFormat.Format32bppArgb))
                {
                    var d2 = o.LockBits(r, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(outp, 0, d2.Scan0, n);
                    o.UnlockBits(d2);
                    o.Save(diffPath, ImageFormat.Png);
                }
            }
            return (double)changed / (x.Width * x.Height);
        }
    }
}
'@

New-Item -ItemType Directory -Force $Out | Out-Null
$rows = @()
foreach ($f in Get-ChildItem $Head -Filter *.png | Sort-Object Name) {
    $b = Join-Path $Base $f.Name
    if (-not (Test-Path $b)) { $rows += "| $($f.Name) | new window |"; continue }
    $share = [ShotDiff]::Compare($f.FullName, (Resolve-Path $b).Path, (Join-Path (Resolve-Path $Out).Path $f.Name))
    if ($share -lt 0) { $rows += "| $($f.Name) | size changed |" }
    elseif ($share -eq 0) { $rows += "| $($f.Name) | same |" }
    elseif ($share -lt 0.005) { $rows += "| $($f.Name) | same apart from $([math]::Round($share * 100, 2))% (timestamps, live data) |" }
    else { $rows += "| $($f.Name) | **changed: $([math]::Round($share * 100, 1))% of pixels** |"; Write-Output "::notice title=Screenshot changed::$($f.Name) differs from the base branch in $([math]::Round($share * 100, 1))% of its pixels" }
}
foreach ($f in Get-ChildItem $Base -Filter *.png -ErrorAction SilentlyContinue) {
    if (-not (Test-Path (Join-Path $Head $f.Name))) { $rows += "| $($f.Name) | gone (no longer captured) |" }
}
$summary = @('### Screenshots compared with the base branch', '', '| Window | Result |', '|---|---|') + $rows +
    @('', 'Changed pixels are shown in red in the `screenshot-diffs` artifact.')
$file = if ($env:GITHUB_STEP_SUMMARY) { $env:GITHUB_STEP_SUMMARY } else { 'summary.md' }
$summary -join "`n" | Tee-Object -FilePath $file -Append
