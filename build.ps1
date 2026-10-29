param([switch]$Offline, [switch]$Install)
$ErrorActionPreference='Stop'
$projectDir=$PSScriptRoot
$gameDir=[System.IO.Path]::GetFullPath((Join-Path $projectDir '..\..'))
$arguments=@('build',(Join-Path $projectDir 'UltrawideToggle.csproj'),'-c','Release','--nologo','-p:NuGetAudit=false')
if($Offline){$arguments+='-p:OfflineBuild=true'}
& dotnet @arguments
if($LASTEXITCODE -ne 0){throw 'Build failed'}
$stage=Join-Path $projectDir 'artifacts\package'
New-Item -ItemType Directory -Path (Join-Path $stage 'Code') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'bin\Release\net8.0\UltrawideToggle.dll') -Destination (Join-Path $stage 'Code\UltrawideToggle.dll') -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'everest.yaml') -Destination $stage -Force
Copy-Item -LiteralPath (Join-Path $projectDir 'docs\Player-README.txt') -Destination (Join-Path $stage 'README.txt') -Force
$oldReadme=Join-Path $stage 'README.md'
if(Test-Path -LiteralPath $oldReadme){Remove-Item -LiteralPath $oldReadme}
Copy-Item -LiteralPath (Join-Path $projectDir 'LICENSE') -Destination $stage -Force
New-Item -ItemType Directory -Path (Join-Path $stage 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'docs\Validation.md') -Destination (Join-Path $stage 'docs\Validation.md') -Force
New-Item -ItemType Directory -Path (Join-Path $stage 'Dialog') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectDir 'Dialog\English.txt') -Destination (Join-Path $stage 'Dialog\English.txt') -Force
$archive=Join-Path $projectDir 'artifacts\UltrawideToggle-1.0.2.zip'
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
if($Install){
 $destination=Join-Path $gameDir 'Mods\UltrawideToggle.zip'
 if(Test-Path -LiteralPath $destination){
  try{
   $lockCheck=[System.IO.File]::Open($destination,[System.IO.FileMode]::Open,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None)
   $lockCheck.Dispose()
  }catch [System.IO.IOException]{throw 'Celeste holds the mod ZIP open. Close Celeste, then rerun build.ps1 -Offline -Install.'}
 }
 Copy-Item -LiteralPath $archive -Destination $destination -Force
}
Write-Output $archive
