# End-to-end installer test: serve a release feed + the mod zip from a local TCP socket,
# then drive the installer's CLI (the unelevated `cli` example) against a throwaway game
# folder - install, assert the shipped file set and the backup of a pre-existing file,
# uninstall, assert the folder is back to pristine. Nothing touches the real game.
#   tools\installer-e2e.ps1 [-Zip releases\Echopunks-vX.Y.Z.zip] [-Cli installer\target\release\examples\cli.exe]
# Build both first: build_release.ps1 and `cargo build --release --example cli` in installer\.

param(
    [string]$Zip,
    [string]$Cli,
    [int]$Port = 8781
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

[xml]$props = Get-Content (Join-Path $root "Directory.Build.props")
$version = $props.SelectSingleNode("/Project/PropertyGroup/Version").InnerText.Trim()
if (-not $Zip) { $Zip = Join-Path $root "releases\Echopunks-v$version.zip" }
if (-not $Cli) { $Cli = Join-Path $root "installer\target\release\examples\cli.exe" }
foreach ($f in @($Zip, $Cli)) { if (-not (Test-Path $f)) { throw "Not found: $f" } }
$zipName = Split-Path -Leaf $Zip
$sha = (Get-FileHash -Algorithm SHA256 $Zip).Hash.ToLower()

$game = Join-Path ([IO.Path]::GetTempPath()) ("echopunks-e2e-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $game | Out-Null
Set-Content -LiteralPath (Join-Path $game "EXAPUNKS.exe") -Value "not the game" -NoNewline
Set-Content -LiteralPath (Join-Path $game "Renderer_D3D11.dll") -Value "" -NoNewline
# A pre-existing file the zip overwrites: must be backed up on install, restored on uninstall.
Set-Content -LiteralPath (Join-Path $game "steam_appid.txt") -Value "original" -NoNewline

$feed = '[{"tag_name":"v' + $version + '","prerelease":false,"body":"e2e notes","assets":[{"name":"' + $zipName +
    '","browser_download_url":"http://127.0.0.1:' + $Port + '/' + $zipName + '","digest":"sha256:' + $sha + '"}]}]'

# A minimal HTTP/1.1 server on a plain socket: no URL reservation needed, unlike HttpListener.
$server = Start-Job -ArgumentList $Port, $Zip, $zipName, $feed -ScriptBlock {
    param($port, $zipPath, $zipName, $feed)
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, $port)
    $listener.Start()
    $zipBytes = [IO.File]::ReadAllBytes($zipPath)
    $feedBytes = [Text.Encoding]::UTF8.GetBytes($feed)
    while ($true) {
        $client = $listener.AcceptTcpClient()
        $stream = $client.GetStream()
        $reader = New-Object IO.StreamReader($stream)
        $requestLine = $reader.ReadLine()
        while ($true) { $l = $reader.ReadLine(); if ($null -eq $l -or $l -eq "") { break } }
        $path = ($requestLine -split " ")[1]
        if ($path -eq "/releases") { $body = $feedBytes; $type = "application/json"; $status = "200 OK" }
        elseif ($path -eq "/$zipName") { $body = $zipBytes; $type = "application/zip"; $status = "200 OK" }
        else { $body = [Text.Encoding]::ASCII.GetBytes("not found"); $type = "text/plain"; $status = "404 Not Found" }
        $head = "HTTP/1.1 $status`r`nContent-Type: $type`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n"
        $headBytes = [Text.Encoding]::ASCII.GetBytes($head)
        $stream.Write($headBytes, 0, $headBytes.Length)
        $stream.Write($body, 0, $body.Length)
        $stream.Flush()
        $client.Close()
    }
}

$failures = New-Object System.Collections.Generic.List[string]
function Assert-True([bool]$cond, [string]$what) { if ($cond) { Write-Host "  ok  $what" } else { Write-Host "  FAIL $what" -ForegroundColor Red; $failures.Add($what) } }

try {
    Start-Sleep -Milliseconds 500
    $env:EXAPUNKS_DIR = $game
    $env:ECHOPUNKS_INSTALLER_RELEASES_URL = "http://127.0.0.1:$Port/releases"

    Write-Host "== install =="
    $out = @("y", "1", "4") | & $Cli 2>&1
    $out | ForEach-Object { Write-Host "    $_" }
    $shipped = @("EXAPUNKS.exe.config", "Echopunks.dll", "Echopunks.Module.dll", "Mono.Cecil.dll", "0Harmony.dll",
        "prism.dll", "steam_appid.txt", "Echopunks\namemap.tsv", "Echopunks\locale\enGB\ui.json",
        "Echopunks\docs\twn-1.html", "Echopunks\docs\twn-2.html", "Echopunks\docs\twn-epilogue.html",
        "Echopunks\docs\images\twn-1-fig01.png", "Echopunks\install.json")
    foreach ($rel in $shipped) { Assert-True (Test-Path (Join-Path $game $rel)) "installed $rel" }
    Assert-True ((Get-Content -Raw (Join-Path $game "steam_appid.txt")) -eq "716490") "steam_appid.txt is the shipped one"
    Assert-True ((@(Get-ChildItem -Recurse -File (Join-Path $game "Echopunks\backups") -ErrorAction SilentlyContinue | Where-Object Name -eq "steam_appid.txt")).Count -eq 1) "pre-existing steam_appid.txt backed up"
    Assert-True (-not (Test-Path (Join-Path $game "Mono.CSharp.dll"))) "no dev tooling shipped"

    Write-Host "== uninstall =="
    $out = @("y", "3", "y", "4") | & $Cli 2>&1
    $out | ForEach-Object { Write-Host "    $_" }
    foreach ($rel in $shipped) { if ($rel -ne "steam_appid.txt") { Assert-True (-not (Test-Path (Join-Path $game $rel))) "removed $rel" } }
    Assert-True (-not (Test-Path (Join-Path $game "Echopunks"))) "mod folder gone"
    Assert-True ((Get-Content -Raw (Join-Path $game "steam_appid.txt")) -eq "original") "pre-existing steam_appid.txt restored"
    Assert-True ((Get-ChildItem $game).Count -eq 3) "game folder back to its three original files"
}
finally {
    Stop-Job $server -ErrorAction SilentlyContinue; Remove-Job $server -Force -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force $game -ErrorAction SilentlyContinue
    Remove-Item Env:\EXAPUNKS_DIR -ErrorAction SilentlyContinue
    Remove-Item Env:\ECHOPUNKS_INSTALLER_RELEASES_URL -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) { Write-Host "E2E FAILED: $($failures.Count) assertion(s)" -ForegroundColor Red; exit 1 }
Write-Host "E2E passed." -ForegroundColor Green
