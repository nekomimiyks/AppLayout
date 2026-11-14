param([string]$ApplicationPath)
$ErrorActionPreference = 'Stop'
$project = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not $ApplicationPath) { $ApplicationPath = Join-Path $project 'AppLayout.exe' }
$sources = @((Get-ChildItem -LiteralPath (Join-Path $project 'src\core') -Filter '*.cs').FullName)
$sources += @((Get-ChildItem -LiteralPath (Join-Path $project 'variants\Ledger') -Filter '*.cs').FullName)
$sources += (Join-Path $PSScriptRoot 'Verification.cs')
$output = Join-Path $PSScriptRoot 'Verification.exe'
$icon = Join-Path $project 'src\app.ico'
& $compiler /nologo /target:exe /main:AppLayout.Verification "/out:$output" "/resource:$icon,AppLayout.AppIcon.ico" `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Verification compile failed' }
$stdout = Join-Path $PSScriptRoot 'stdout.log'
$stderr = Join-Path $PSScriptRoot 'stderr.log'
$process = Start-Process -FilePath $output -ArgumentList @(('"' + $ApplicationPath + '"'), 'ledger') `
    -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
$process.Handle | Out-Null
if (-not $process.WaitForExit(60000)) { $process.Kill(); throw 'Verification timed out' }
$process.Refresh()
Get-Content -LiteralPath $stdout -Encoding UTF8
Get-Content -LiteralPath $stderr -Encoding UTF8
if ($process.ExitCode -ne 0) { throw "Verification failed: $($process.ExitCode)" }
