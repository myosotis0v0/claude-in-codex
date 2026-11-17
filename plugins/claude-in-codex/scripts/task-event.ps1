param([switch]$Test)
$ErrorActionPreference='Stop'
$folder=$null
$receivedTicks=[DateTime]::UtcNow.Ticks
$kind='';$sessionHash='';$turnHash='';$operation='ReadPayload'
function Hash-Identifier([string]$Value) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Value))).Replace('-','').Substring(0,24) }
    finally { $sha.Dispose() }
}
function Write-Rejection([string]$Reason) {
    if(-not $folder) { return }
    $mutex=$null;$held=$false
    try {
        $full=[IO.Path]::GetFullPath($folder).TrimEnd([char[]]@('\','/')).ToUpperInvariant()
        $mutex=[Threading.Mutex]::new($false,('Local\ClaudeInCodex.TaskRejections.'+(Hash-Identifier $full)))
        try { $held=$mutex.WaitOne(50) } catch [Threading.AbandonedMutexException] { $held=$true }
        if(-not $held) { return }
        [IO.Directory]::CreateDirectory($folder) | Out-Null
        $version=''
        try {
            $manifest=Join-Path (Split-Path $PSScriptRoot -Parent) '.codex-plugin/plugin.json'
            $v=(ConvertFrom-Json -InputObject ([IO.File]::ReadAllText($manifest))).version
            if($v -is [string] -and $v -cmatch '^\d+\.\d+\.\d+$') { $version=$v }
        } catch { }
        $record=[ordered]@{Utc=[DateTime]::UtcNow.ToString('O');Stage='Writer';Reason=$Reason;
            Event=$(if($kind -is [string] -and $kind -cin @('UserPromptSubmit','Stop','Interrupt','SessionEnd')){$kind}else{''});
            Session=$sessionHash;Turn=$turnHash;SignalTicks=$receivedTicks;CurrentTurn='';CurrentEvent='';CurrentTicks=0;Pid=$PID;Version=$version}
        $line=($record | ConvertTo-Json -Compress)+"`n"
        $path=Join-Path $folder 'rejections.jsonl';$previous=Join-Path $folder 'rejections.previous.jsonl'
        if([IO.File]::Exists($path) -and ([IO.FileInfo]::new($path)).Length+[Text.Encoding]::UTF8.GetByteCount($line) -gt 65536) {
            if([IO.File]::Exists($previous)) { [IO.File]::Delete($previous) }
            [IO.File]::Move($path,$previous)
        }
        [IO.File]::AppendAllText($path,$line,[Text.UTF8Encoding]::new($false))
    } catch { } finally { if($held) { $mutex.ReleaseMutex() };if($mutex) { $mutex.Dispose() } }
}
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
    try { $raw=$reader.ReadToEnd();$operation='ParsePayload';$payload=ConvertFrom-Json -InputObject $raw }
    finally { $reader.Dispose() }
    $kind=$payload.hook_event_name
    $session=$payload.session_id
    $turn=$payload.turn_id
    if($session -is [string] -and $session.Length -gt 0 -and $session.Length -le 4096) { $sessionHash=Hash-Identifier $session }
    if($turn -is [string] -and $turn.Length -gt 0 -and $turn.Length -le 4096) { $turnHash=Hash-Identifier $turn }
    if($kind -isnot [string] -or $kind -cnotin @('UserPromptSubmit','Stop','Interrupt','SessionEnd')) { Write-Rejection 'UnknownEvent';return }
    if(-not $sessionHash) { Write-Rejection 'InvalidSession';return }
    if($payload.agent_id -is [string] -and $payload.agent_id.Length -gt 0) { Write-Rejection 'Subagent';return }
    if($kind -cne 'SessionEnd' -and -not $turnHash) { Write-Rejection 'InvalidTurn';return }
    $signal=[ordered]@{Session=$sessionHash;Turn=$turnHash;Event=$kind;Ticks=$receivedTicks}
    $operation='WriteQueue'
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $path=Join-Path $folder ("{0}-{1}.json" -f $receivedTicks,[Guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($path+'.tmp',($signal | ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false))
    [IO.File]::Move($path+'.tmp',$path)
} catch {
    if($_.Exception.GetBaseException() -is [Text.DecoderFallbackException]) { Write-Rejection 'InvalidUtf8' }
    elseif($operation -eq 'ParsePayload') { Write-Rejection 'MalformedPayload' }
    elseif($operation -eq 'WriteQueue') { Write-Rejection 'DeliveryFailure' }
    else { Write-Rejection 'PayloadReadFailure' }
} finally {
    # Advisory only: no prompt context, blocking decision, continuation or child process.
    Write-Output '{}'
}
