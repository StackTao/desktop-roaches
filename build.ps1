param([switch]$Test, [switch]$NormalHead)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$source = Join-Path $projectDir 'src\Program.cs'
$motion = Join-Path $projectDir 'src\Motion.cs'
$asset = Join-Path $projectDir 'assets\roach-face-integrated.png'
$limbs = Join-Path $projectDir 'assets\roach-limbs-reference.png'
$output = Join-Path $projectDir '桌面蟑螂.exe'
$variant = @()
if ($NormalHead) {
    $asset = Join-Path $projectDir 'assets\roach-body-reference.png'
    $output = Join-Path $projectDir '桌面蟑螂-正常头小号版.exe'
    $variant = @('/define:NORMAL_HEAD')
}
$manifest = Join-Path $projectDir 'src\app.manifest'
& $compiler @variant /nologo /optimize+ /target:winexe /platform:anycpu "/out:$output" "/win32manifest:$manifest" "/resource:$asset,roach-body.png" "/resource:$limbs,roach-limbs.png" /r:System.Drawing.dll /r:System.Windows.Forms.dll $source $motion (Join-Path $projectDir 'src\RoachRenderer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
Write-Output "Built: $output"
if ($Test) {
    $testOutput = Join-Path $projectDir 'tests\MotionTests.exe'
    & $compiler @variant /nologo /optimize+ /target:exe "/out:$testOutput" /r:System.Drawing.dll $motion (Join-Path $projectDir 'tests\MotionTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Motion test build failed.' }
    & $testOutput
    if ($LASTEXITCODE -ne 0) { throw 'Motion tests failed.' }
    $dynamicsOutput = Join-Path $projectDir 'tests\DynamicsTests.exe'
    & $compiler @variant /nologo /optimize+ /target:exe "/out:$dynamicsOutput" /r:System.Drawing.dll $motion (Join-Path $projectDir 'tests\DynamicsTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Dynamics test build failed.' }
    & $dynamicsOutput
    if ($LASTEXITCODE -ne 0) { throw 'Dynamics tests failed.' }
    $animationOutput = Join-Path $projectDir 'tests\AnimationTests.exe'
    & $compiler @variant /nologo /optimize+ /target:exe "/out:$animationOutput" "/resource:$asset,roach-body.png" "/resource:$limbs,roach-limbs.png" /r:System.Drawing.dll $motion (Join-Path $projectDir 'src\RoachRenderer.cs') (Join-Path $projectDir 'tests\AnimationTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Animation test build failed.' }
    & $animationOutput (Join-Path $projectDir 'tests\animation')
    if ($LASTEXITCODE -ne 0) { throw 'Animation tests failed.' }
    $nativeOutput = Join-Path $projectDir 'tests\NativeSmoke.exe'
    & $compiler @variant /nologo /optimize+ /target:exe /main:NativeSmoke "/out:$nativeOutput" "/win32manifest:$manifest" "/resource:$asset,roach-body.png" "/resource:$limbs,roach-limbs.png" /r:System.Drawing.dll /r:System.Windows.Forms.dll $source $motion (Join-Path $projectDir 'src\RoachRenderer.cs') (Join-Path $projectDir 'tests\NativeSmoke.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Native test build failed.' }
    & $nativeOutput (Join-Path $projectDir 'tests')
    if ($LASTEXITCODE -ne 0) { throw 'Native integration tests failed.' }
}
