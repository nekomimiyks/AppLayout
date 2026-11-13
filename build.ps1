param([string]$Variant = 'A', [string]$Out = 'AppLayout.exe')
# AppLayout.exe をビルドする（Windows標準の .NET Framework 4 の C# コンパイラを使用。追加インストール不要）
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

# アイコン（PNG入りICO）を生成
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $here 'src\app.ico'
$sizes = 16, 32, 48, 256
if (-not (Test-Path $ico)) {
$pngs = foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s, $s
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $m = $s * 0.06; $gap = $s * 0.06; $half = ($s - 2 * $m - $gap) / 2
  $cols = @([System.Drawing.Color]::FromArgb(37, 99, 235), [System.Drawing.Color]::FromArgb(16, 185, 129), [System.Drawing.Color]::FromArgb(245, 158, 11), [System.Drawing.Color]::FromArgb(236, 72, 153))
  for ($i = 0; $i -lt 4; $i++) {
    $x = $m + ($i % 2) * ($half + $gap); $y = $m + [math]::Floor($i / 2) * ($half + $gap)
    $b = New-Object System.Drawing.SolidBrush $cols[$i]; $g.FillRectangle($b, [single]$x, [single]$y, [single]$half, [single]$half); $b.Dispose()
    $b = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(90, 255, 255, 255)); $g.FillRectangle($b, [single]$x, [single]$y, [single]$half, [single]($half * 0.22)); $b.Dispose()
  }
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
  , $ms.ToArray()
}
$fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()
}

$outPath = Join-Path $here $Out
$variantDir = Join-Path $here (Join-Path 'variants' $Variant)
if (-not (Test-Path -LiteralPath $variantDir -PathType Container)) { throw "UI variant not found: $variantDir" }
$src = @(Get-ChildItem -LiteralPath (Join-Path $here 'src\core') -Filter '*.cs' | ForEach-Object { $_.FullName })
$src += @(Get-ChildItem -LiteralPath $variantDir -Filter '*.cs' | ForEach-Object { $_.FullName })
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$outPath" "/win32icon:$ico" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.Runtime.Serialization.dll $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Build complete: $outPath"