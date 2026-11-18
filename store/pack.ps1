param(
    [Parameter(Mandatory)][string]$Out,
    [string]$MakeAppx = $env:MAKEAPPX
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$Out = [IO.Path]::GetFullPath($Out)
if (-not $MakeAppx) {
    $sdk = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path -LiteralPath $sdk) {
        $MakeAppx = Get-ChildItem -LiteralPath $sdk -Directory |
            Sort-Object Name -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
            Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (-not $MakeAppx) { throw 'Specify makeappx.exe with -MakeAppx or MAKEAPPX.' }
$parent = Split-Path -Parent $Out
New-Item -ItemType Directory -Path $parent -Force | Out-Null
$parent = (Resolve-Path -LiteralPath $parent).Path
$stage = Join-Path $parent ('msix-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & (Join-Path $root 'build.ps1') -Store -Out (Join-Path $stage 'AppLayout.exe')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination $stage
    $assets = Join-Path $stage 'Assets'
    New-Item -ItemType Directory -Path $assets | Out-Null
    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Image]::FromFile((Join-Path $root 'src\assets\applayout.png'))
    try {
        $sizes = @{ 'StoreLogo.png' = 50; 'Square150x150Logo.png' = 150; 'Square44x44Logo.png' = 44 }
        foreach ($name in $sizes.Keys) {
            $size = $sizes[$name]
            $bitmap = New-Object Drawing.Bitmap $size,$size
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($icon, 0, 0, $size, $size)
                $bitmap.Save((Join-Path $assets $name), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $icon.Dispose() }
    & $MakeAppx pack /d $stage /p $Out /o
    if ($LASTEXITCODE -ne 0) { throw 'MakeAppx packaging failed.' }
} finally {
    $resolved = (Resolve-Path -LiteralPath $stage).Path
    if ((Split-Path -Parent $resolved) -eq $parent -and (Split-Path -Leaf $resolved) -match '^msix-stage-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
