param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Compiler = 'ISCC.exe'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Path $out -Force | Out-Null
$stage = Join-Path $out ('installer-stage-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
try {
    & (Join-Path $root 'build.ps1') -Out (Join-Path $stage 'AppLayout.exe')
    & $Compiler "/DAppLayoutVersion=$Version" "/DAppLayoutPayload=$stage" "/O$out" (Join-Path $PSScriptRoot 'AppLayout.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
} finally {
    Remove-Item -LiteralPath (Join-Path $stage 'AppLayout.exe') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stage
}
