param([string]$Out = 'AppLayout.exe', [switch]$Store, [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
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
$resources = @(Get-ChildItem -LiteralPath (Join-Path $here 'src\locales') -Filter '*.json' | ForEach-Object { '/resource:' + $_.FullName + ',AppLayout.Locales.' + $_.Name })
$storeArgs = @()
if ($Store) {
    $metadata = Join-Path $env:WINDIR 'System32\WinMetadata'
    $runtime = Join-Path (Split-Path -Parent $csc) 'System.Runtime.WindowsRuntime.dll'
    $storeArgs = @('/define:STORE_BUILD', "/r:$runtime",
        ('/r:' + (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll')),
        ('/r:' + (Join-Path $metadata 'Windows.ApplicationModel.winmd')),
        ('/r:' + (Join-Path $metadata 'Windows.Foundation.winmd')))
}
$versionResource = $null
try {
    if ($Version -and -not $Store) {
        $versionResource = Join-Path ([IO.Path]::GetDirectoryName($outPath)) ('.appversion-' + [Guid]::NewGuid().ToString('N'))
        [IO.File]::WriteAllText($versionResource, $Version, [Text.Encoding]::ASCII)
        $resources += "/resource:$versionResource,AppLayout.ReleaseVersion"
    }
    & $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$outPath" "/win32icon:$ico" "/resource:$ico,AppLayout.AppIcon.ico" `
      /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
      /r:System.Runtime.Serialization.dll $storeArgs $resources $src
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally {
    if ($versionResource) { Remove-Item -LiteralPath $versionResource -ErrorAction SilentlyContinue }
}
Write-Output "Build complete: $outPath"
