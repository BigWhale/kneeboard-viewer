# Publishes the plugin and assembles the .sdPlugin bundle under bin/.
$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'Kneeboard Viewer.StreamDeck.csproj'
$bundleName = 'com.bigwhale.kneeboardviewer.sdPlugin'
$outRoot = Join-Path $PSScriptRoot 'bin\sdplugin'
$bundle = Join-Path $outRoot $bundleName
$publish = Join-Path $PSScriptRoot 'bin\publish'

if (Test-Path $bundle) { Remove-Item -Recurse -Force $bundle }
New-Item -ItemType Directory -Force -Path $bundle | Out-Null

dotnet publish $proj -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish

Copy-Item (Join-Path $publish '*') -Destination $bundle -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'manifest.json') -Destination $bundle -Force
Copy-Item (Join-Path $PSScriptRoot 'Icons') -Destination $bundle -Recurse -Force

Write-Host "Plugin bundle assembled at $bundle"
