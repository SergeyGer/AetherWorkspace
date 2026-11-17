#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Minimal local OBS Studio WebSocket 5.x controller (v1.1 AW-055).
.DESCRIPTION
  Connects only to an explicitly configured localhost OBS WebSocket endpoint, authenticates with a
  local secret from the environment, and starts/stops recording. Secrets are never written to the
  repository or evidence. Dot-source this file from Record-Demo.ps1.
#>

function Get-ObsSettings {
    $url = $env:AETHER_OBS_WEBSOCKET_URL
    if ([string]::IsNullOrWhiteSpace($url)) { $url = 'ws://127.0.0.1:4455' }
    $password = $env:AETHER_OBS_WEBSOCKET_PASSWORD
    if ([string]::IsNullOrWhiteSpace($password)) {
        throw 'AETHER_OBS_WEBSOCKET_PASSWORD is not set (local OBS WebSocket password; never commit it).'
    }

    return [pscustomobject]@{ Url = $url; Password = $password }
}

function Receive-ObsMessage($socket) {
    $buffer = New-Object byte[] 1048576
    $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
    $stream = New-Object System.IO.MemoryStream
    do {
        $result = $socket.ReceiveAsync($segment, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
            throw 'OBS WebSocket closed the connection.'
        }
        $stream.Write($buffer, 0, $result.Count)
    } while (-not $result.EndOfMessage)

    $text = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
    return $text | ConvertFrom-Json
}

function Send-ObsFrame($socket, $payload) {
    $json = $payload | ConvertTo-Json -Depth 8 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
    $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
}

function Connect-Obs {
    $settings = Get-ObsSettings
    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    $socket.ConnectAsync([Uri]$settings.Url, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $hello = Receive-ObsMessage $socket
    if ($hello.op -ne 0) { throw "Unexpected OBS hello message: $($hello.d)" }

    $identify = @{ op = 1; d = @{ rpcVersion = 1 } }
    $auth = $hello.d.authentication
    if ($auth) {
        $secret = [Convert]::ToBase64String([System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($settings.Password + $auth.salt)))
        $authentication = [Convert]::ToBase64String([System.Security.Cryptography.SHA256]::HashData(
            [System.Text.Encoding]::UTF8.GetBytes($secret + $auth.challenge)))
        $identify.d.authentication = $authentication
    }

    Send-ObsFrame $socket $identify
    $identified = Receive-ObsMessage $socket
    if ($identified.op -ne 2) { throw "OBS authentication/identify failed: $($identified.d | ConvertTo-Json -Compress)" }
    return [pscustomobject]@{ Socket = $socket; RequestId = 0 }
}

function Invoke-ObsRequest($connection, [string] $requestType, $requestData = @{}) {
    $connection.RequestId++
    $id = "aether-$($connection.RequestId)"
    Send-ObsFrame $connection.Socket @{ op = 6; d = @{ requestType = $requestType; requestId = $id; requestData = $requestData } }
    while ($true) {
        $message = Receive-ObsMessage $connection.Socket
        if ($message.op -eq 7 -and $message.d.requestId -eq $id) {
            if (-not $message.d.requestStatus.result) {
                throw "OBS request '$requestType' failed: $($message.d.requestStatus.comment)"
            }

            return [pscustomobject]@{ Data = $message.d.responseData; Status = $message.d.requestStatus }
        }
    }
}

function Start-ObsRecording($connection) {
    Invoke-ObsRequest $connection 'StartRecord' | Out-Null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        $status = (Invoke-ObsRequest $connection 'GetRecordStatus').Data
        if ($status.outputActive) { return }
        Start-Sleep -Milliseconds 200
    }
    throw 'OBS did not report an active recording within 10 seconds.'
}

function Stop-ObsRecording($connection) {
    $result = Invoke-ObsRequest $connection 'StopRecord'
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        $status = (Invoke-ObsRequest $connection 'GetRecordStatus').Data
        if (-not $status.outputActive) {
            $path = if ($result.Data.outputPath) { $result.Data.outputPath } else { $status.outputPath }
            return $path
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'OBS did not stop the recording within 15 seconds.'
}

function Disconnect-Obs($connection) {
    try { $connection.Socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [System.Threading.CancellationToken]::None).GetAwaiter().GetResult() } catch { }
    $connection.Socket.Dispose()
}
