param([switch]$Test)
$ErrorActionPreference='Stop'
$folder=$null
$receivedTicks=[DateTime]::UtcNow.Ticks
try {
    if($Test) {
        if([string]::IsNullOrWhiteSpace($env:CLAUDE_TEST_OUTPUT) -or -not [IO.Path]::IsPathRooted($env:CLAUDE_TEST_OUTPUT)) {
            throw [InvalidOperationException]::new('An absolute isolated test directory is required.')
        }
        $folder=$env:CLAUDE_TEST_OUTPUT
    } else {
        $folder=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ClaudeInCodex\task-events'
    }
    # Hook stdin is UTF-8 JSON, independent of the Windows console code page.
    $reader=[IO.StreamReader]::new([Console]::OpenStandardInput(),[Text.UTF8Encoding]::new($false,$true),$false)
    try { $payload=ConvertFrom-Json -InputObject ($reader.ReadToEnd()) }
    finally { $reader.Dispose() }
    $kind=$payload.hook_event_name
    $session=$payload.session_id
    $turn=$payload.turn_id
    if($kind -isnot [string] -or $kind -cnotin @('UserPromptSubmit','Stop','Interrupt','SessionEnd') -or
       $session -isnot [string] -or $session.Length -eq 0 -or $session.Length -gt 4096 -or
       ($payload.agent_id -is [string] -and $payload.agent_id.Length -gt 0)) { return }
    if($kind -cne 'SessionEnd' -and ($turn -isnot [string] -or $turn.Length -eq 0 -or $turn.Length -gt 4096)) { return }
    $sha=[Security.Cryptography.SHA256]::Create()
    try {
        $sessionHash=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($session))).Replace('-','').Substring(0,24)
        $turnHash=if($turn -is [string] -and $turn.Length -gt 0) {
            [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($turn))).Replace('-','').Substring(0,24)
        } else { '' }
    } finally { $sha.Dispose() }
    $signal=[ordered]@{Session=$sessionHash;Turn=$turnHash;Event=$kind;Ticks=$receivedTicks}
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path=Join-Path $folder ("{0}-{1}.json" -f $receivedTicks,[Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($path+'.tmp',($signal | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($path+'.tmp',$path)
} catch {
    # Bounded error categories only; never persist payloads, exception messages or identifiers.
    if($folder) { try {
        [IO.Directory]::CreateDirectory($folder) | Out-Null
        $log=Join-Path $folder 'delivery-errors.log'
        if(-not [IO.File]::Exists($log) -or ([IO.FileInfo]::new($log)).Length -lt 16384) {
            [IO.File]::AppendAllText($log,([DateTime]::UtcNow.ToString('O')+' '+$_.Exception.GetType().Name+[Environment]::NewLine))
        }
    } catch { } }
} finally {
    # Advisory only: no prompt context, blocking decision, continuation or child process.
    Write-Output '{}'
}
