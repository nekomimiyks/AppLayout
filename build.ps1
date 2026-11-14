param([string]$Out = 'AppLayout.exe')
# Windows標準の .NET Framework 4 C# コンパイラだけでビルドする。
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

# 採用したPNGからタスクトレイ～高DPI表示用のICOを作る。
Add-Type -AssemblyName System.Drawing
$ico = Join-Path $here 'src\app.ico'
$source = Join-Path $here 'src\assets\applayout.png'
if (-not (Test-Path -LiteralPath $source)) { throw "Icon source not found: $source" }
if (-not (Test-Path -LiteralPath $ico) -or (Get-Item -LiteralPath $source).LastWriteTimeUtc -gt (Get-Item -LiteralPath $ico).LastWriteTimeUtc) {
    $original = [System.Drawing.Image]::FromFile($source)
    try {
        $sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
        $pngs = foreach ($size in $sizes) {
            $bmp = New-Object System.Drawing.Bitmap $size, $size
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $ms = New-Object System.IO.MemoryStream
            try {
                $g.Clear([System.Drawing.Color]::Transparent)
                $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $g.DrawImage($original, (New-Object System.Drawing.Rectangle 0,0,$size,$size))
                $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
                , $ms.ToArray()
            } finally { $g.Dispose(); $bmp.Dispose(); $ms.Dispose() }
        }
        $writer = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($ico))
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
            $offset = 6 + 16 * $sizes.Count
            for ($i = 0; $i -lt $sizes.Count; $i++) {
                $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
                $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
                $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([uint16]1); $writer.Write([uint16]32)
                $writer.Write([uint32]$pngs[$i].Length); $writer.Write([uint32]$offset)
                $offset += $pngs[$i].Length
            }
            foreach ($png in $pngs) { $writer.Write($png) }
        } finally { $writer.Dispose() }
    } finally { $original.Dispose() }
}

$outPath = if ([System.IO.Path]::IsPathRooted($Out)) { $Out } else { Join-Path $here $Out }
$uiDir = Join-Path $here 'src\ui'

$src = @(Get-ChildItem -LiteralPath (Join-Path $here 'src\core') -Filter '*.cs' | ForEach-Object { $_.FullName })
$src += @(Get-ChildItem -LiteralPath $uiDir -Filter '*.cs' | ForEach-Object { $_.FullName })
& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$outPath" "/win32icon:$ico" "/resource:$ico,AppLayout.AppIcon.ico" `
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
  /r:System.Runtime.Serialization.dll $src
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
Write-Output "Build complete: $outPath"
