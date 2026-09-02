# Packs Build/Windows into the zip itch.io wants.
#
#   powershell -NoProfile -File Tools\make_windows_zip.ps1
#
# Run it after "Iteration Room > Build Windows Player". Output: Build/iteration-windows.zip.
#
# WHY THIS EXISTS RATHER THAN Compress-Archive - the same reason make_webgl_zip.ps1 does, and
# it is worth stating again because the symptom is different here and just as quiet. Both
# Compress-Archive and [IO.Compression.ZipFile]::CreateFromDirectory write
# Path.DirectorySeparatorChar into the entry names, so on Windows every path comes out as
# "Iteration\Iteration_Data\globalgamemanagers". The ZIP spec requires forward slashes.
# Extractors that take the backslash literally create ONE file whose name contains a backslash
# instead of a directory tree - and for a player build that means an .exe next to nothing,
# which fails on launch rather than on download. The entry names are built by hand below.
#
# It also verifies the ARCHIVE rather than the folder. Checking the folder is what let the
# broken WebGL zip through in August: the folder was correct the whole time.
#
# EVERYTHING GOES UNDER ONE TOP-LEVEL FOLDER, unlike the WebGL zip, which must have
# index.html at its root. A player build has no such requirement, and somebody unzipping this
# by hand should get one folder in their Downloads rather than nine loose items.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Relative to this script, so it does not care where it is run from.
$repo = Split-Path -Parent $PSScriptRoot
$root = Join-Path $repo 'Build\Windows'
$dest = Join-Path $repo 'Build\iteration-windows.zip'
$prefix = 'Iteration'

if (-not (Test-Path $root)) {
    throw "No Windows build at $root. Run 'Iteration Room > Build Windows Player' first."
}

# Unity leaves this beside the player and its own name says not to ship it.
$burst = Join-Path $root 'Iteration_BurstDebugInformation_DoNotShip'
if (Test-Path $burst) {
    Remove-Item $burst -Recurse -Force
    Write-Output "removed Iteration_BurstDebugInformation_DoNotShip"
}

if (Test-Path $dest) { Remove-Item $dest -Force }

Write-Output "packing $root ..."
$zip = [System.IO.Compression.ZipFile]::Open($dest, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem -Path $root -Recurse -File) {
        $rel = $prefix + '/' + $f.FullName.Substring($root.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $f.FullName, $rel, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $zip.Dispose()
}

$check = [System.IO.Compression.ZipFile]::OpenRead($dest)
try {
    $names = @($check.Entries | ForEach-Object { $_.FullName })
    $bad = @($names | Where-Object { $_ -like '*\*' })

    if ($bad.Count -gt 0) { throw "$($bad.Count) entries still contain a backslash: $($bad -join ', ')" }

    # The three things a Windows player cannot run without. Named individually rather than
    # counted, because a zip with the right number of wrong files is the failure this is for.
    foreach ($needed in @("$prefix/Iteration.exe", "$prefix/UnityPlayer.dll",
                          "$prefix/Iteration_Data/globalgamemanagers")) {
        if ($names -notcontains $needed) { throw "$needed is missing from the archive" }
    }

    $mb = [math]::Round((Get-Item $dest).Length / 1MB, 1)
    Write-Output "--- $($names.Count) entries, 0 backslashes, everything under $prefix/"
    Write-Output "--- $dest ($mb MB)"

    # itch.io's browser upload refuses a file over 1 GB. butler has no such limit, so the
    # advice differs by which route this is going out on.
    if ($mb -gt 1024) {
        Write-Warning "Over 1 GB - itch.io's browser uploader will refuse this. Use butler."
    }
} finally {
    $check.Dispose()
}
