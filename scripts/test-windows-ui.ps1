param([Parameter(Mandatory)][string]$Executable, [switch]$HostFlow, [string]$OtherBuild)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes
$stage = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../obj/ui-tests'))
New-Item -ItemType Directory -Force $stage | Out-Null
$runId = "ui-" + [guid]::NewGuid().ToString("N")
$runRoot = Join-Path $stage $runId
$root = Join-Path $runRoot 'StarComms ověření' 
New-Item -ItemType Directory $root | Out-Null
$copy = Join-Path $root 'StarComms.exe'
Copy-Item -LiteralPath $Executable -Destination $copy
$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $runRoot 'cache'
$start = [Diagnostics.ProcessStartInfo]::new($copy)
$start.WorkingDirectory = $env:TEMP
$start.UseShellExecute = $false
$profilePath = Join-Path $runRoot 'profil'
$start.ArgumentList.Add('--test-profile')
$start.ArgumentList.Add($profilePath)
$p = [Diagnostics.Process]::Start($start)
function FindControl([string]$type,[string]$name) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        $p.Refresh()
        if ($p.HasExited) { throw "Process exited: $($p.ExitCode)" }
        if ($p.MainWindowHandle.ToInt64()) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
            $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id))
            foreach ($surface in $windows) {
            foreach ($item in $surface.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition)) {
                if ($item.Current.ControlType.ProgrammaticName -eq "ControlType.$type" -and $item.Current.Name -eq $name ) { return $item }
            }
            }
        }
        Start-Sleep -Milliseconds 100
    } while ($timer.Elapsed.TotalSeconds -lt 20)
    $labels = $window.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } | Select-Object -Unique
    throw "Missing $type $name, visible controls: $($labels -join ', ')"
}
function InvokeButton([string]$name) {
    $control = FindControl Button $name
    $control.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
}
try {
    $null = FindControl Button 'Připojit'
    "PASS start screen from standalone EXE in a path containing spaces and accents"
    InvokeButton 'Zvuk a nastavení'
    $shipMode = (FindControl CheckBox 'Hlasová aktivace pro posádku').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    $commandMode = (FindControl CheckBox 'Hlasová aktivace pro kapitány').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($shipMode.Current.ToggleState -ne 'On' -or $commandMode.Current.ToggleState -ne 'Off') { throw 'Unexpected voice mode defaults' }
    $threshold = (FindControl Slider 'Práh hlasové aktivace').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)
    if ($threshold.Current.Value -ne -35) { throw 'Unexpected microphone threshold' }
    $threshold.SetValue(-48)
    $shipMode.Toggle()
    $commandMode.Toggle()
    InvokeButton 'Hotovo'
    $null = FindControl Button 'Připojit'
    $saved = Get-Content (Join-Path $profilePath 'settings.json') -Raw | ConvertFrom-Json
    if ($saved.ActivationThresholdDb -ne -48) { throw 'Microphone threshold was not saved' }
    if ($saved.ShipAutomatic -ne $false -or $saved.CommandAutomatic -ne $true) { throw 'Voice modes not saved' }
    InvokeButton 'Zvuk a nastavení'
    'PASS independent voice modes and persisted settings'
    $box = FindControl CheckBox 'Logs on'
    $surface=[Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
    $device=$surface.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'DeviceStatus'))
    if ($device) { "WASAPI: $($device.Current.Name)" }
    if ($device -and $device.Current.Name -like '*výstup připraven*') {
        InvokeButton 'Vyzkoušet zvuk'
        $null=FindControl Text 'Přehrání testovacího tónu dokončeno.'
        "PASS WASAPI player opened and completed test tone (no listening assessment)"
    }
    $toggle = $box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if ($toggle.Current.ToggleState -ne 'Off') { throw 'For this test use a clean Windows profile.' }
    if (@(Get-ChildItem $root -Filter '*.log').Count) { throw 'Unexpected log on startup' }
    "PASS default Logs on is off"
    $toggle.Toggle()
    Start-Sleep -Milliseconds 450
    $logs = @(Get-ChildItem $root -Filter '*.log')
    if ($logs.Count -ne 1) { throw "Expected one log next to EXE, found $($logs.Count)" }
    $toggle.Toggle()
    Start-Sleep -Milliseconds 300
    $stream=[IO.File]::Open($logs[0].FullName,'Open','Read','None')
    $stream.Dispose()
    "PASS log is beside EXE when CWD differs and file is closed after disabling"
    InvokeButton 'Hotovo'
    if ($HostFlow) {
        $edit = FindControl Edit 'Tvoje přezdívka'
        $edit.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Testovací host')
        InvokeButton 'Chci hostovat'
        $null = FindControl Button 'Správa flotily'
        "PASS published EXE hosting"
        $hostProcess = $p
        InvokeButton 'Správa flotily'
        InvokeButton 'Přidat loď'
        $shipName = FindControl Edit 'Název lodi'
        $shipName.GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Polaris')
        InvokeButton 'Vytvořit loď'
        InvokeButton 'Zavřít'
        InvokeButton 'Podrobnosti připojení'
        $null = FindControl Button 'Kopírovat připojení'
        $windows = [Windows.Automation.AutomationElement]::RootElement.FindAll([Windows.Automation.TreeScope]::Children,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id))
        $labels = foreach ($surface in $windows) { $surface.FindAll([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name } }
        $detail = $labels | Where-Object { $_ -match 'TCP i UDP port: (\d+)' } | Select-Object -First 1
        $port = [regex]::Match($detail, 'port: (\d+)').Groups[1].Value
        $pin = [regex]::Match($detail, '[0-9a-fA-F]{64}').Value
        if (!$port -or !$pin) { throw 'Host port/fingerprint missing' }
        InvokeButton 'Zavřít'
        $hostShip = FindControl ListItem 'Polaris'
        $hostShip.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        InvokeButton 'Přestoupit sem'
        $null = FindControl Text 'Tvoje loď'
        $playerProfile = "$profilePath-player"
        $start.ArgumentList[1] = $playerProfile
        if ($OtherBuild) { $start.FileName = $OtherBuild }
        $player = [Diagnostics.Process]::Start($start)
        try {
            $p = $player
            (FindControl Edit 'Tvoje přezdívka').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue('Testovací pilot')
            (FindControl Edit 'Adresa nebo pozvánka od hostitele').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue("starcomms://join?v=1&host=127.0.0.1&port=$port&fp=$pin")
            InvokeButton 'Připojit'
            $p = $hostProcess
            InvokeButton 'Přijmout'
            $p = $player
            if ($OtherBuild) {
                $a=(Get-Item -LiteralPath $copy).VersionInfo.ProductVersion
                $b=(Get-Item -LiteralPath $OtherBuild).VersionInfo.ProductVersion
                if ($a -eq $b) { throw 'Version mismatch test needs two different builds' }
                $null=FindControl Text "Máš jiné sestavení než hostitel: $b / $a"
                "PASS connected across compatible builds with both versions visible"
            }
            $ship = FindControl ListItem 'Polaris' 
            $ship.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
            InvokeButton 'Přestoupit sem'
            $null = FindControl Text 'Tvoje loď'
            "PASS second EXE joined with pinned invitation, approved and boarded Polaris"
            if (!$OtherBuild) {
                InvokeButton 'Zvuk a nastavení'
                (FindControl CheckBox 'Hlasová aktivace pro posádku').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
                InvokeButton 'Hotovo'
            }
            $surface = [Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
            $talk = $surface.FindFirst([Windows.Automation.TreeScope]::Descendants,
                [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,'ShipTalk'))
            if ($talk.Current.IsEnabled) {
                # Send the window its own hold/release messages. No input is sent to the foreground game or desktop.
                Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class StarCommsWindowTest { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, int msg, IntPtr wParam, IntPtr lParam); }' -ErrorAction SilentlyContinue
                $talk.SetFocus()
                try {
                    [StarCommsWindowTest]::PostMessage($player.MainWindowHandle,0x100,32,0x00390001) | Out-Null
                    $p=$hostProcess
                    $null = FindControl Text 'Posádka · Testovací pilot'
                    "PASS WASAPI capture via window PTT, protected UDP, Opus decoder and receiver mixer"
                } finally {
                    [StarCommsWindowTest]::PostMessage($player.MainWindowHandle,0x101,32,[IntPtr](-1070006271)) | Out-Null
                    $p=$player
                }
            } else { "NOT RUN live capture transfer: PTT unavailable" }

            InvokeButton 'Odpojit / ukončit hostování'
            $null = FindControl Button 'Připojit'
        } finally {
            if (!$player.HasExited) { $player.CloseMainWindow() | Out-Null; if (!$player.WaitForExit(5000)) { $player.Kill() } }
            $p = $hostProcess
        }
        InvokeButton 'Odpojit / ukončit hostování'
        InvokeButton 'Ukončit hostování'
        $null=FindControl Button 'Chci hostovat'
        "PASS confirmed shutdown of host"
    }
    $p.CloseMainWindow() | Out-Null
    if (!$p.WaitForExit(6000) -or $p.ExitCode -ne 0) { throw 'Application did not exit cleanly' }
    "PASS normal shutdown"
    $success = $true
} finally {
    if (!$p.HasExited) { $p.CloseMainWindow() | Out-Null; if (!$p.WaitForExit(5000)) { $p.Kill() } }
    if ($success) {
        $resolved = (Resolve-Path -LiteralPath $runRoot).Path
        if ((Split-Path -Parent $resolved) -eq $stage -and (Split-Path -Leaf $resolved) -eq $runId) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
