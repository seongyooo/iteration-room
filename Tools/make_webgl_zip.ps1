# Packs Build/WebGL into the zip itch.io wants.
#
#   powershell -NoProfile -File Tools\make_webgl_zip.ps1
#
# Run it after "Iteration Room > Build WebGL Player". Output: Build/iteration-webgl.zip,
# with index.html at the archive root, which is what itch.io looks for.
#
# WHY THIS EXISTS RATHER THAN Compress-Archive.
#
# Neither Compress-Archive nor [IO.Compression.ZipFile]::CreateFromDirectory can be used
# here. Both write Path.DirectorySeparatorChar into the entry names, so on Windows every
# path comes out as "Build\WebGL.loader.js". The ZIP spec requires forward slashes, and
# itch.io's extractor takes the backslash literally: it creates a single file whose NAME
# contains a backslash instead of a Build/ directory. index.html then loads fine and every
# asset it references 404s - which is exactly what shipped on 2026-08-11 and had to be
# re-uploaded. The entry names are therefore built by hand below.
#
# It also verifies the ARCHIVE afterwards rather than the folder. Checking the folder is
# what let the broken zip through: the folder was correct the whole time.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Relative to this script, so it does not care where it is run from.
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'Build\WebGL'
$dest = Join-Path $repo 'Build\iteration-webgl.zip'

if (-not (Test-Path $root)) {
    throw "No WebGL build at $root. Run 'Iteration Room > Build WebGL Player' first."
}

# Unity leaves this beside the player and its own name says not to ship it.
$burst = Join-Path $root 'Iteration_BurstDebugInformation_DoNotShip'
if (Test-Path $burst) {
    Remove-Item $burst -Recurse -Force
    Write-Output "removed Iteration_BurstDebugInformation_DoNotShip"
}

if (Test-Path $dest) { Remove-Item $dest -Force }

$zip = [System.IO.Compression.ZipFile]::Open($dest, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem -Path $root -Recurse -File) {
        $rel = $f.FullName.Substring($root.Length + 1).Replace('\', '/')
        # The .unityweb payloads are Brotli already and the wasm with them. Deflating them
        # a second time costs minutes and saves nothing.
        $level = if ($rel -match '\.(unityweb|wasm)$') {
            [System.IO.Compression.CompressionLevel]::NoCompression
        } else {
            [System.IO.Compression.CompressionLevel]::Optimal
        }
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $rel, $level)
    }
} finally {
    $zip.Dispose()
}

$check = [System.IO.Compression.ZipFile]::OpenRead($dest)
try {
    $names = @($check.Entries | ForEach-Object { $_.FullName })
    $bad = @($names | Where-Object { $_ -like '*\*' })

    if ($bad.Count -gt 0) { throw "$($bad.Count) entries still contain a backslash: $($bad -join ', ')" }
    if ($names -notcontains 'index.html') { throw "index.html is not at the archive root" }
    if ($names -notcontains 'Build/WebGL.loader.js') { throw "Build/WebGL.loader.js is missing" }

    Write-Output ($names -join "`n")
    Write-Output "--- $($names.Count) entries, 0 backslashes, index.html at root"
} finally {
    $check.Dispose()
}

Write-Output "--- $dest ($([math]::Round((Get-Item $dest).Length / 1MB, 1)) MB)"
