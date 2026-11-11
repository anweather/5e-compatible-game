@echo off
rem The PowerShell below the #PS line runs as a script block (.ps1 files are blocked on this PC).
powershell -NoProfile -Command "& ([scriptblock]::Create(((Get-Content -Raw -LiteralPath '%~f0') -split '(?m)^#PS\r?$')[1])) %*"
exit /b %errorlevel%
#PS
# unblock-editor.cmd [-List] [-Button "Don't Save"] [-Seconds 30]
# Rescue for a Unity Editor frozen behind a "Scene(s) Have Been Modified" (or similar save) dialog: every probe and
# `unity command` then times out with 504 until someone answers it. This finds those dialogs on Unity's windows
# (Windows UI Automation: no keystrokes, no mouse moves) and presses "Don't Save" on each one, until none is left.
#   -List     only print the dialogs it finds; press nothing.
#   -Button   the button to press (default "Don't Save"; "Cancel" keeps the scene open and unsaved).
#   -Seconds  how long to keep looking for the next stacked dialog after the last one (default 8).
# It never kills Unity, never presses Save, and only touches windows of Unity.exe whose title matches
# "Have Been Modified", "Save Scene" or "Save changes". Run it from any prompt: tools\unblock-editor.cmd
# Why it happens: a failed Prototype > Build Chemistry Yard (for example with unity/Temp/map = nine, whose
# world/maps/nine/themes folder isn't in git) leaves a modified Untitled scene open; the next build or probe then
# asks to save it, and the dialogs stack up. Build the yard with Temp/map absent, set the map after.
param([switch]$List, [string]$Button = "Don't Save", [int]$Seconds = 8)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Name Win32Click -Namespace Unblock -MemberDefinition '[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);'
$AE = [Windows.Automation.AutomationElement]
$TS =[Windows.Automation.TreeScope]
$titleRx = 'Have Been Modified|Save Scene|Save changes|Save modified'

function Get-Dialogs {
  $pids = @(Get-Process -Name Unity -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
  if (-not $pids) { return @() }
  $found = @()
  foreach ($w in $AE::RootElement.FindAll($TS::Children, [Windows.Automation.Condition]::TrueCondition)) {
    if ($pids -notcontains $w.Current.ProcessId) { continue }
    # the dialog is its own top-level window, or a child window of the Editor's main window
    $cands = @($w) + @($w.FindAll($TS::Descendants, (New-Object Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [Windows.Automation.ControlType]::Window))))
    foreach ($c in $cands) { if ($c.Current.Name -match $titleRx) { $found += $c } }
  }
  return $found
}

# A classic Windows dialog (#32770): its buttons are Win32 'Button' controls, which UI Automation shows as panes.
function Get-Buttons($dlg) {
  return @($dlg.FindAll($TS::Descendants, [Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.ClassName -eq 'Button' })
}
function Get-Button($dlg, $name) {
  return Get-Buttons $dlg | Where-Object { $_.Current.Name -eq $name } | Select-Object -First 1
}

if (-not (Get-Process -Name Unity -ErrorAction SilentlyContinue)) { Write-Host 'No Unity.exe is running.'; exit 0 }
$pressed = 0
$quietSince = Get-Date
while ($true) {
  $dialogs = @(Get-Dialogs)
  if ($List) {
    if ($dialogs.Count -eq 0) { Write-Host 'No save dialog found.' }
    foreach ($d in $dialogs) {
      $btns = Get-Buttons $d | ForEach-Object { $_.Current.Name }
      Write-Host ("Dialog: '{0}' (pid {1}); buttons: {2}" -f $d.Current.Name, $d.Current.ProcessId, ($btns -join ', '))
    }
    exit 0
  }
  if ($dialogs.Count -gt 0) {
    $d = $dialogs[0]
    $b = Get-Button $d $Button
    if (-not $b) {
      Write-Host ("Dialog '{0}' has no '{1}' button; leaving it alone." -f $d.Current.Name, $Button)
      exit 1
    }
    [void][Unblock.Win32Click]::PostMessage([IntPtr]$b.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)   # BM_CLICK: presses that button; no mouse, no keystrokes
    $pressed++
    Write-Host ("Pressed '{0}' on '{1}' ({2})." -f $Button, $d.Current.Name, $pressed)
    $quietSince = Get-Date
    Start-Sleep -Milliseconds 700
    continue
  }
  if (((Get-Date) - $quietSince).TotalSeconds -ge $Seconds) { break }
  Start-Sleep -Milliseconds 500
}
if ($pressed -eq 0) { Write-Host 'No save dialog found. (If the Editor still answers 504, look at its window; it may be a different modal.)' }
else { Write-Host ("Done: answered {0} dialog(s). Check: unity command --timeout 15 script eval '1+1'" -f $pressed) }
