#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Minimal local OBS Studio WebSocket 5.x controller (v1.1 AW-055).
.DESCRIPTION
  Connects only to an explicitly configured localhost OBS WebSocket endpoint, authenticates with a
  local secret from the environment, and starts/stops recording. Secrets are never written to the
  repository or evidence. Dot-source this file from Record-Demo.ps1 or for a local sanity check.
#>

$ErrorActionPreference = 'Stop'

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
    if ($socket.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw "OBS WebSocket is not open (state: $($socket.State)); reconnect and verify the WebSocket password."
    }

    $buffer = New-Object byte[] 1048576
    $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$buffer)
    $stream = New-Object System.IO.MemoryStream
    $timeout = [System.Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(15))
    try {
        do {
            try {
                $result = $socket.ReceiveAsync($segment, $timeout.Token).GetAwaiter().GetResult()
            }
            catch {
                throw "OBS WebSocket receive failed: $($_.Exception.Message). Reconnect and verify the local password; no secrets are printed."
            }

            if ($result.MessageType -eq [System.Net.WebSockets.WebSocketMessageType]::Close) {
                throw 'OBS WebSocket closed the connection; verify the WebSocket password and that OBS is running.'
            }

            if ($result.Count -gt 0) {
                $stream.Write($buffer, 0, $result.Count)
            }
        } while (-not $result.EndOfMessage)

        $text = [System.Text.Encoding]::UTF8.GetString($stream.ToArray())
        return $text | ConvertFrom-Json
    }
    finally {
        $timeout.Dispose()
        $stream.Dispose()
    }
}

function Send-ObsFrame($socket, $payload) {
    if ($socket.State -ne [System.Net.WebSockets.WebSocketState]::Open) {
        throw "OBS WebSocket is not open (state: $($socket.State)); reconnect before sending commands."
    }

    $json = $payload | ConvertTo-Json -Depth 8 -Compress
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    $segment = New-Object System.ArraySegment[byte] -ArgumentList @(,$bytes)
    $socket.SendAsync($segment, [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
}

function Connect-Obs {
    $settings = Get-ObsSettings
    $socket = [System.Net.WebSockets.ClientWebSocket]::new()
    try {
        $socket.ConnectAsync([Uri]$settings.Url, [System.Threading.CancellationToken]::None).GetAwaiter().GetResult()
        $hello = Receive-ObsMessage $socket
        if ($hello.op -ne 0) { throw "Unexpected OBS hello message (op $($hello.op))." }

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
        if ($identified.op -ne 2) {
            throw "OBS authentication/identify failed (op $($identified.op)); verify AETHER_OBS_WEBSOCKET_PASSWORD matches OBS Tools > WebSocket Server Settings."
        }

        return [pscustomobject]@{ Socket = $socket; RequestId = 0 }
    }
    catch {
        try { $socket.Dispose() } catch { }
        throw
    }
}

function Invoke-ObsRequest($connection, [string] $requestType, $requestData = @{}) {
    $connection.RequestId++
    $id = "aether-$($connection.RequestId)"
    Send-ObsFrame $connection.Socket @{ op = 6; d = @{ requestType = $requestType; requestId = $id; requestData = $requestData } }

    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $message = Receive-ObsMessage $connection.Socket
        if ($message.op -eq 7 -and $message.d.requestId -eq $id) {
            if (-not $message.d.requestStatus.result) {
                throw "OBS request '$requestType' failed: $($message.d.requestStatus.comment)"
            }

            return [pscustomobject]@{ Data = $message.d.responseData; Status = $message.d.requestStatus }
        }
        # OBS event frames (op 5) are intentionally ignored while waiting for our response.
    }

    throw "OBS request '$requestType' timed out after 20 seconds."
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
    $before = (Invoke-ObsRequest $connection 'GetRecordStatus').Data
    $result = Invoke-ObsRequest $connection 'StopRecord'
    $deadline = (Get-Date).AddSeconds(15)
    while ((Get-Date) -lt $deadline) {
        $status = (Invoke-ObsRequest $connection 'GetRecordStatus').Data
        if (-not $status.outputActive) {
            $path = $result.Data.outputPath
            if ([string]::IsNullOrWhiteSpace($path)) { $path = $before.outputPath }
            if ([string]::IsNullOrWhiteSpace($path)) { $path = $status.outputPath }
            return $path
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'OBS did not stop the recording within 15 seconds.'
}

function Disconnect-Obs($connection) {
    if ($null -eq $connection) { return }
    try {
        if ($connection.Socket.State -eq [System.Net.WebSockets.WebSocketState]::Open) {
            $connection.Socket.CloseAsync([System.Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [System.Threading.CancellationToken]::None).GetAwaiter().GetResult() | Out-Null
        }
    }
    catch {
        // Best effort close; the caller only cares that the recording has stopped.
    }
    finally {
        $connection.Socket.Dispose()
    }
}
