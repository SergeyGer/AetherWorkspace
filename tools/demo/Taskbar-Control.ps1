#!/usr/bin/env pwsh
<#
.SYNOPSIS
  Temporarily hides/restores the Windows taskbar for a clean local demo capture (v1.1 AW-055).
.DESCRIPTION
  Uses the documented shell SHAppBarMessage ABM_GETSTATE/ABM_SETSTATE API. The original state is
  captured by the caller and restored in a finally block; no permanent setting is written and no
  process is restarted. Windows only.
#>

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class AetherTaskbarControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uCallbackMessage;
        public int uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    private const uint ABM_GETSTATE = 4;
    private const uint ABM_SETSTATE = 10;

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern uint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    public static int GetState()
    {
        var data = new APPBARDATA();
        data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    public static void SetState(int state)
    {
        var data = new APPBARDATA();
        data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
        data.lParam = (IntPtr)state;
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }
}
'@

function Get-TaskbarState {
    if (-not $IsWindows) { throw 'Taskbar control is Windows-only.' }
    return [AetherTaskbarControl]::GetState()
}

function Set-TaskbarAutoHide {
    param([bool] $Hidden)
    if (-not $IsWindows) { throw 'Taskbar control is Windows-only.' }
    $state = [AetherTaskbarControl]::GetState()
    # ABS_AUTOHIDE = 0x1; preserve any other flags for the restore path.
    if ($Hidden) { $state = $state -bor 0x1 } else { $state = $state -band (-bnot 0x1) }
    [AetherTaskbarControl]::SetState($state)
}

function Restore-TaskbarState {
    param([int] $State)
    if (-not $IsWindows) { return }
    [AetherTaskbarControl]::SetState($State)
}
