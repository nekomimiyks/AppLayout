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
    & (Join-Path $root 'build.ps1') -Out (Join-Path $stage 'AppLayout.exe') -Version $Version
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $csc)) {
        $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    & $csc /nologo /target:winexe /optimize+ "/out:$(Join-Path $stage 'CloseRunning.exe')" (Join-Path $PSScriptRoot 'CloseRunning.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Upgrade helper compilation failed.' }
    & $Compiler "/DAppLayoutVersion=$Version" "/DAppLayoutPayload=$stage" "/O$out" (Join-Path $PSScriptRoot 'AppLayout.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
} finally {
    Remove-Item -LiteralPath (Join-Path $stage 'AppLayout.exe') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $stage 'CloseRunning.exe') -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $stage
}
