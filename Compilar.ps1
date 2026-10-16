param([string]$DotnetPath = 'dotnet')
$ErrorActionPreference='Stop'
& $DotnetPath build (Join-Path $PSScriptRoot 'src\MineirosRotinas.csproj') -c Release -o (Join-Path $PSScriptRoot 'dist') -clp:ErrorsOnly
if($LASTEXITCODE -ne 0){throw 'Build failed. Do not install.'}
