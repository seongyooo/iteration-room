param([string]$PlayerPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'Build/Windows/Iteration.exe'))
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IterationWindowProbe {
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
'@
$null = [IterationWindowProbe]::SetProcessDpiAwarenessContext([IntPtr](-4))
$projectRoot = Split-Path $PSScriptRoot -Parent
$log = Join-Path $projectRoot 'TestResults/windows-startup.log'
$player = Start-Process -FilePath $PlayerPath -ArgumentList "-logFile `"$log`"" -WindowStyle Hidden -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do {
        Start-Sleep -Milliseconds 500
        $player.Refresh()
        if ($player.HasExited) { throw 'Player exited before creating its window.' }
    } while ($player.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($player.MainWindowHandle -eq [IntPtr]::Zero) { throw 'No player window was observable; fullscreen startup remains unverified.' }
    # Let initial scene loading settle before reading native window geometry.
    Start-Sleep -Seconds 5
    $rect = New-Object IterationWindowProbe+Rect
    $client = New-Object IterationWindowProbe+Rect
    $info = New-Object IterationWindowProbe+MonitorInfo
    $info.Size = [Runtime.InteropServices.Marshal]::SizeOf($info)
    $handle = $player.MainWindowHandle
    if (-not [IterationWindowProbe]::GetWindowRect($handle, [ref]$rect)) { throw 'Cannot read player window bounds.' }
    if (-not [IterationWindowProbe]::GetClientRect($handle, [ref]$client)) { throw 'Cannot read player client bounds.' }
    $monitor = [IterationWindowProbe]::MonitorFromWindow($handle, 2)
    if (-not [IterationWindowProbe]::GetMonitorInfo($monitor, [ref]$info)) { throw 'Cannot read monitor bounds.' }
    $full = $rect.Left -eq $info.Monitor.Left -and $rect.Top -eq $info.Monitor.Top -and
        $rect.Right -eq $info.Monitor.Right -and $rect.Bottom -eq $info.Monitor.Bottom -and
        $client.Right -eq ($rect.Right - $rect.Left) -and $client.Bottom -eq ($rect.Bottom - $rect.Top)
    [PSCustomObject]@{ FullscreenBounds = $full; WindowWidth = $rect.Right - $rect.Left;
        WindowHeight = $rect.Bottom - $rect.Top; ClientWidth = $client.Right; ClientHeight = $client.Bottom;
        Log = $log } | ConvertTo-Json | Tee-Object -FilePath (Join-Path $projectRoot 'TestResults/windows-startup.json')
    if (-not $full) { throw 'Window does not fill its monitor without borders. Check saved preferences and the player log.' }
}
finally {
    if (-not $player.HasExited) {
        $null = $player.CloseMainWindow()
        if (-not $player.WaitForExit(5000)) { $player.Kill() }
    }
    $player.Dispose()
}
