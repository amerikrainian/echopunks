# Build the distributable mod zip: the Release build's shipping file set laid out as the
# game folder - EXAPUNKS.exe.config, Echopunks.dll, Echopunks.Module.dll, Mono.Cecil.dll,
# 0Harmony.dll, prism.dll, steam_appid.txt, and the Echopunks\ folder (namemap.tsv + locale\ +
# docs\ - the zine text documents TRASH WORLD NEWS opens - + the player manual).
# The zip root IS the game folder, so the installer (and a manual user) extracts it straight
# into the game dir. A Release build carries no dev tooling (no dev server, no Mono.CSharp).
#
# Adapted from the Non-Visual Calculus installer by Rashad Naqeeb (MIT),
# https://github.com/rashadnaqeeb/NonVisualCalculus - by way of the Harkest Dungeon
# installer, https://github.com/amerikrainian/harkest-dungeon.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$propsPath = Join-Path $scriptDir "Directory.Build.props"
$releaseDir = Join-Path $scriptDir "releases"
$stageDir = Join-Path $scriptDir "obj\release-stage"

[xml]$props = Get-Content $propsPath
$versionNode = $props.SelectSingleNode("/Project/PropertyGroup/Version")
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "Could not read Version from $propsPath"
}
$version = $versionNode.InnerText.Trim()

$hostOutDir = Join-Path $scriptDir "bin\Release"
$moduleOutDir = Join-Path $scriptDir "module\bin\Release"
$nameMap = Join-Path $scriptDir "module\obj\Release\namemap.tsv"
$localeDir = Join-Path $scriptDir "module\assets\locale"
$docsDir = Join-Path $scriptDir "docs\game"
$manual = Join-Path $scriptDir "docs\echopunks manual.html"
$prismDll = Join-Path $scriptDir "third_party\prism\prism.dll"
$configFile = Join-Path $scriptDir "deploy\EXAPUNKS.exe.config"
$zipPath = Join-Path $releaseDir "Echopunks-v$version.zip"

foreach ($required in @($prismDll, $configFile, $localeDir, $docsDir, $manual, (Join-Path $scriptDir "game\EXAPUNKS-deob.exe"))) {
    if (-not (Test-Path $required)) {
        throw "Required file not found: $required (the module build needs game\EXAPUNKS-deob.exe - run tools\prepare-game.ps1)"
    }
}

Push-Location $scriptDir
try {
    # Every shipped dll is named explicitly below, but start the output dirs empty anyway so a
    # stale assembly can never be mistaken for a fresh one.
    foreach ($dir in @($hostOutDir, $moduleOutDir)) {
        if (Test-Path $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    }

    dotnet build Echopunks.sln -c Release -v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE"
    }

    $hostDll = Join-Path $hostOutDir "Echopunks.dll"
    $moduleDll = Join-Path $moduleOutDir "Echopunks.Module.dll"
    $cecilDll = Join-Path $hostOutDir "Mono.Cecil.dll"
    $harmonyDll = Join-Path $hostOutDir "0Harmony.dll"
    foreach ($required in @($hostDll, $moduleDll, $cecilDll, $harmonyDll, $nameMap)) {
        if (-not (Test-Path $required)) {
            throw "Release build output not found: $required"
        }
    }
    if (Test-Path (Join-Path $hostOutDir "Mono.CSharp.dll")) {
        throw "Mono.CSharp.dll in the Release output: dev tooling leaked into the shipping build."
    }

    if (Test-Path $stageDir) {
        Remove-Item -LiteralPath $stageDir -Recurse -Force
    }
    New-Item -ItemType Directory -Force $stageDir | Out-Null
    New-Item -ItemType Directory -Force $releaseDir | Out-Null

    # The same file set the Debug post-build targets deploy (see Echopunks.csproj DeployMod and
    # module/Echopunks.Module.csproj DeployModule), minus the dev REPL.
    Copy-Item -LiteralPath $configFile -Destination $stageDir
    Copy-Item -LiteralPath $hostDll -Destination $stageDir
    Copy-Item -LiteralPath $moduleDll -Destination $stageDir
    Copy-Item -LiteralPath $cecilDll -Destination $stageDir
    Copy-Item -LiteralPath $harmonyDll -Destination $stageDir
    Copy-Item -LiteralPath $prismDll -Destination $stageDir
    # Steam appid so a launch that didn't come from Steam isn't bounced through it
    # (SteamAPI.RestartAppIfNecessary); Bootstrap rewrites it at runtime as a fallback.
    Set-Content -LiteralPath (Join-Path $stageDir "steam_appid.txt") -Value "716490" -NoNewline -Encoding ascii

    $modDir = Join-Path $stageDir "Echopunks"
    New-Item -ItemType Directory -Force $modDir | Out-Null
    Copy-Item -LiteralPath $nameMap -Destination $modDir
    # Packaging pattern adapted from SayTheSpire2:
    # https://github.com/bradjrenshaw/say-the-spire2
    Copy-Item -Path $localeDir -Destination (Join-Path $modDir "locale") -Recurse
    # The zine documents (docs\game\*.html + images\): TRASH WORLD NEWS's Digital Version buttons
    # open these instead of the game's text-locked PDFs (module/src/Screens/TrashWorldNewsScreen.cs).
    New-Item -ItemType Directory -Force (Join-Path $modDir "docs\images") | Out-Null
    Copy-Item -Path (Join-Path $docsDir "*.html") -Destination (Join-Path $modDir "docs")
    Copy-Item -Path (Join-Path $docsDir "images\*") -Destination (Join-Path $modDir "docs\images")
    # The player manual sits at the top of the mod folder, where a user browsing the game dir finds it.
    Copy-Item -LiteralPath $manual -Destination $modDir

    if (Test-Path $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -Force

    Remove-Item -LiteralPath $stageDir -Recurse -Force

    Write-Host "Release zip: $zipPath"
}
finally {
    Pop-Location
}
