param([string]$ThreadId)
$ErrorActionPreference = 'Stop'
$pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'codex-ipc', [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
function Send-Frame($value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($value | ConvertTo-Json -Depth 30 -Compress))
    $pipe.Write([BitConverter]::GetBytes([uint32]$bytes.Length), 0, 4)
    $pipe.Write($bytes, 0, $bytes.Length)
    $pipe.Flush()
}
function Read-Exact([int]$length) {
    $buffer = [byte[]]::new($length)
    $position = 0
    while ($position -lt $length) {
        $task = $pipe.ReadAsync($buffer, $position, $length-$position)
        if (-not $task.Wait(4000)) { throw 'Read timeout' }
        if ($task.Result -le 0) { throw 'Pipe closed' }
        $position += $task.Result
    }
    return ,$buffer
}
function Read-Frame {
    $header = Read-Exact 4
    $length = [BitConverter]::ToUInt32($header, 0)
    if ($length -gt 33554432) { throw 'Oversized frame' }
    [Text.Encoding]::UTF8.GetString((Read-Exact $length)) | ConvertFrom-Json -Depth 100
}
function Request($method,$parameters,$version,$target) {
    $requestId = [guid]::NewGuid().ToString()
    $request = @{type='request';requestId=$requestId;sourceClientId=$script:clientId;method=$method;params=$parameters;version=$version;timeoutMs=4000}
    if ($target) { $request.targetClientId=$target }
    Send-Frame $request
    for ($i=0;$i -lt 100;$i++) {
        $reply = Read-Frame
        if ($reply.type -eq 'client-discovery-request') {Send-Frame @{type='client-discovery-response';requestId=$reply.requestId;response=@{canHandle=$false}}}
        if ($reply.type -eq 'response' -and $reply.requestId -eq $requestId) {
            if ($reply.resultType -ne 'success') { throw ('Request rejected: '+$reply.error) }
            return $reply
        }
    }
    throw 'No response'
}
try {
    $pipe.Connect(3000)
    $script:clientId='initializing-client'
    $init = Request 'initialize' @{clientType='codex-quota-pills-inspector'} 0 $null
    $script:clientId=$init.result.clientId
    Write-Output 'IPC initialize: OK'
    if (-not $ThreadId) {
        $latest = Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.codex/sessions') -Filter 'rollout-*.jsonl' -Recurse | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        $reader = [IO.StreamReader]::new([IO.File]::Open($latest.FullName,'Open','Read','ReadWrite'))
        try {$ThreadId=($reader.ReadLine()|ConvertFrom-Json).payload.id} finally {$reader.Dispose()}
    }
    $owner = (Request 'thread-owner-discovery' @{hostId='local';conversationId=$ThreadId} 1 $null).handledByClientId
    Send-Frame @{type='broadcast';method='thread-stream-following-changed';sourceClientId=$script:clientId;targetClientIds=@($owner);version=1;params=@{hostId='local';conversationId=$ThreadId;following=$true}}
    for ($i=0;$i -lt 100;$i++) {
        $frame=Read-Frame
        if($frame.type -eq 'client-discovery-request'){Send-Frame @{type='client-discovery-response';requestId=$frame.requestId;response=@{canHandle=$false}}}
        if($frame.method -eq 'thread-stream-state-changed' -and $frame.params.change.type -eq 'snapshot') {
            $state=$frame.params.change.conversationState
            Write-Output ('ProtocolVersion='+$frame.version)
            Write-Output ('StateKeys='+($state.PSObject.Properties.Name -join ','))
            Write-Output ('RuntimeType='+$state.threadRuntimeStatus.type)
            Write-Output ('ResumeState='+$state.resumeState)
            Write-Output ('RequestCount='+$state.requests.Count)
            Write-Output ('HistoryKind='+$state.turnHistory.kind)
            Write-Output ('PermissionKeys='+($state.currentPermissions.PSObject.Properties.Name -join ','))
            break
        }
    }
} finally { $pipe.Dispose() }
