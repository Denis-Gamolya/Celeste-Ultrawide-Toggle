$ErrorActionPreference='Stop'
$gameDir=[System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
& dotnet build (Join-Path $PSScriptRoot 'Tests\GeometryTests.csproj') -c Release -p:NuGetAudit=false --nologo
if($LASTEXITCODE -ne 0){throw 'Geometry test build failed'}
& (Join-Path $gameDir 'piton-runtime\dotnet.exe') exec --runtimeconfig (Join-Path $gameDir 'Celeste.runtimeconfig.json') (Join-Path $PSScriptRoot 'Tests\bin\Release\net8.0\GeometryTests.dll')
if($LASTEXITCODE -ne 0){throw 'Geometry tests failed'}
& dotnet build (Join-Path $PSScriptRoot 'Tests\HookTests\HookTests.csproj') -c Release -p:NuGetAudit=false --nologo
if($LASTEXITCODE -ne 0){throw 'Hook test build failed'}
& (Join-Path $gameDir 'piton-runtime\dotnet.exe') exec --runtimeconfig (Join-Path $gameDir 'Celeste.runtimeconfig.json') (Join-Path $PSScriptRoot 'Tests\HookTests\bin\Release\net8.0\HookTests.dll') $gameDir
if($LASTEXITCODE -ne 0){throw 'Hook integration tests failed'}
