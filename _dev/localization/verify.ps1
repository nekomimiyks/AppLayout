$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$keys = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'keys.json')) | ConvertFrom-Json
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $project 'src\locales') -Filter '*.json') {
    $items = [IO.File]::ReadAllText($file.FullName) | ConvertFrom-Json
    if ($items.Count -ne $keys.Count) { throw "Key count mismatch: $($file.Name)" }
    foreach ($key in $keys) {
        $entry = @($items | Where-Object Key -CEQ $key)
        if ($entry.Count -ne 1 -or [string]::IsNullOrWhiteSpace($entry[0].Value)) { throw "Missing translation: $($file.Name) / $key" }
        $expected = ([regex]::Matches($key, '\{\d+\}') | ForEach-Object Value | Sort-Object) -join ','
        $actual = ([regex]::Matches($entry[0].Value, '\{\d+\}') | ForEach-Object Value | Sort-Object) -join ','
        if ($expected -ne $actual) { throw "Placeholder mismatch: $($file.Name) / $key" }
    }
    Write-Output "$($file.Name): $($items.Count) translations verified"
}
