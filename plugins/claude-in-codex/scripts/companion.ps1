param([ValidateSet('Start','Stop','Status')][string]$Action='Status')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$exe=Join-Path $root 'bin\ClaudeCompanion.exe'
if (!(Test-Path -LiteralPath $exe)) { $exe=Join-Path $root 'artifacts\publish\ClaudeCompanion.exe' }
function Find-Companion {
    Get-Process -Name ClaudeCompanion -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -eq $exe } catch { $false }
    }
}
if ($Action -eq 'Status') {
    $running=@(Find-Companion)
    [pscustomobject]@{Running=($running.Count -gt 0);ProcessIds=@($running.Id);Executable=$exe}
    return
}
if ($Action -eq 'Stop') {
    # Send a close message to the companion only, allowing app-server cleanup.
    $running=@(Find-Companion)
    if ($running.Count -eq 0) { Write-Output 'Companion is not running.'; return }
    Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class ClaudeStop { public delegate bool EnumProc(IntPtr h, IntPtr p); [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc e, IntPtr p); [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p); [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }'
    $ids=@($running.Id)
    [ClaudeStop]::EnumWindows({param($h,$p) $pidValue=0; [void][ClaudeStop]::GetWindowThreadProcessId($h,[ref]$pidValue); if ($ids -contains [int]$pidValue) {[void][ClaudeStop]::PostMessage($h,0x10,[IntPtr]::Zero,[IntPtr]::Zero)}; return $true},[IntPtr]::Zero) | Out-Null
    Write-Output 'Requested companion shutdown.'
    return
}
if (!(Test-Path -LiteralPath $exe)) {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Source install needs .NET 10 SDK, or use a self-contained release package.' }
    & dotnet publish (Join-Path $root 'src\ClaudeCompanion\ClaudeCompanion.csproj') -c Release -o (Join-Path $root 'artifacts\publish') --self-contained false
    if ($LASTEXITCODE -ne 0) { throw 'Companion build failed.' }
}
if (@(Find-Companion).Count -gt 0) { Write-Output 'Companion is already running.'; return }
$p=Start-Process -FilePath $exe -PassThru
Write-Output "Started Claude in Codex (PID $($p.Id))."
