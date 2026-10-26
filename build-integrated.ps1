param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$inputs = @('桌面蟑螂-校准版.exe', '桌面蟑螂-质感版.exe', '桌面蟑螂-fxq版.exe')
$resources = @()
for ($i = 0; $i -lt $inputs.Length; $i++) {
    $inputPath = Join-Path $projectDir $inputs[$i]
    if (-not (Test-Path -LiteralPath $inputPath)) { throw "Missing original version: $inputPath" }
    $resources += "/resource:$inputPath,variant$i.exe"
}
$source = Join-Path $projectDir 'integrated\Program.cs'
$manifest = Join-Path $projectDir 'src\app.manifest'
$output = Join-Path $projectDir '桌面蟑螂-三合一新版.exe'
& $compiler /nologo /optimize+ /target:winexe /platform:anycpu "/win32manifest:$manifest" "/out:$output" /r:System.Drawing.dll /r:System.Windows.Forms.dll @resources $source
if ($LASTEXITCODE -ne 0) { throw 'Integrated application build failed.' }
Write-Output "Built: $output"
if ($Test) {
    $tests = Join-Path $projectDir 'tests\IntegrationTests.exe'
    & $compiler /nologo /optimize+ /target:exe /main:IntegrationTests "/win32manifest:$manifest" "/out:$tests" /r:System.Drawing.dll /r:System.Windows.Forms.dll @resources $source (Join-Path $projectDir 'integrated\IntegrationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Integrated test build failed.' }
    & $tests (Join-Path $projectDir 'tests')
    if ($LASTEXITCODE -ne 0) { throw 'Integrated tests failed.' }
}
