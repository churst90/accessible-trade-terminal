#Requires -Version 5
# Dumps everything Windows knows about COM ports + any Dot-Pad-adjacent USB
# devices. Pure diagnostics, makes no changes. Output is captured by the
# dotpad-diagnose.bat wrapper into dotpad-diagnose.log.

$ErrorActionPreference = 'Continue'

function Section($title) {
    Write-Output ""
    Write-Output "=========================================="
    Write-Output "  $title"
    Write-Output "=========================================="
}

Section "Run info"
Write-Output "Timestamp        : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Output "Computer         : $env:COMPUTERNAME"
Write-Output "User             : $env:USERNAME"
Write-Output "OS               : $((Get-CimInstance Win32_OperatingSystem).Caption) $((Get-CimInstance Win32_OperatingSystem).Version)"
Write-Output "PowerShell       : $($PSVersionTable.PSVersion)"

Section "COM ports — full property dump"
$ports = Get-PnpDevice -Class Ports -PresentOnly -ErrorAction SilentlyContinue
if (-not $ports) {
    Write-Output "(none — no devices in the Ports class are present)"
} else {
    foreach ($p in $ports) {
        $hwIds      = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_HardwareIds        -ErrorAction SilentlyContinue).Data
        $compatIds  = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_CompatibleIds      -ErrorAction SilentlyContinue).Data
        $busDesc    = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_BusReportedDeviceDesc -ErrorAction SilentlyContinue).Data
        $service    = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_Service            -ErrorAction SilentlyContinue).Data
        $driver     = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_DriverVersion      -ErrorAction SilentlyContinue).Data
        $mfg        = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_Manufacturer       -ErrorAction SilentlyContinue).Data
        $location   = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_LocationInfo       -ErrorAction SilentlyContinue).Data
        $parent     = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_Parent             -ErrorAction SilentlyContinue).Data
        $problem    = (Get-PnpDeviceProperty -InstanceId $p.InstanceId -KeyName DEVPKEY_Device_ProblemCode        -ErrorAction SilentlyContinue).Data

        Write-Output "--- $($p.FriendlyName) ---"
        Write-Output "  Status         : $($p.Status)"
        Write-Output "  ProblemCode    : $problem"
        Write-Output "  Manufacturer   : $mfg"
        Write-Output "  BusReportedDesc: $busDesc"
        Write-Output "  Service        : $service"
        Write-Output "  DriverVersion  : $driver"
        Write-Output "  Location       : $location"
        Write-Output "  InstanceId     : $($p.InstanceId)"
        Write-Output "  Parent         : $parent"
        Write-Output "  HardwareIds    : $($hwIds -join ' ; ')"
        Write-Output "  CompatibleIds  : $($compatIds -join ' ; ')"
        Write-Output ""
    }
}

Section "All currently-present USB devices (excluding hubs)"
Get-PnpDevice -PresentOnly -Class USB -ErrorAction SilentlyContinue |
    Where-Object { $_.FriendlyName -notmatch '(?i)hub|root' } |
    Format-Table FriendlyName, Status, InstanceId -AutoSize | Out-String -Width 240 | Write-Output

Section "Anything whose name might be Dot-Pad related"
$patterns = @('*Dot*', '*ESP*', '*Silicon*', '*CP210*', '*FTDI*', '*Serial*', '*Bluetooth*braille*', '*USB-Serial*')
$hits = @()
foreach ($pat in $patterns) {
    $hits += Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -like $pat }
}
$hits | Sort-Object -Unique InstanceId | Format-Table FriendlyName, Status, Class, InstanceId -AutoSize |
    Out-String -Width 240 | Write-Output

Section "Devices currently in an error state"
$errors = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.Status -ne 'OK' }
if (-not $errors) {
    Write-Output "(none — all present devices report Status=OK)"
} else {
    $errors | Format-Table FriendlyName, Status, Class, InstanceId -AutoSize | Out-String -Width 240 | Write-Output
}

Section "SerialPort.GetPortNames() result (what .NET sees)"
try {
    Add-Type -AssemblyName System.IO.Ports -ErrorAction SilentlyContinue
    $names = [System.IO.Ports.SerialPort]::GetPortNames()
    Write-Output ($names -join ', ')
} catch {
    Write-Output "ERROR: $($_.Exception.Message)"
}

Section "Bluetooth LE adapter present?"
try {
    Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
        Where-Object { $_.Class -eq 'Bluetooth' -and $_.FriendlyName -match '(?i)radio|adapter|LE' } |
        Format-Table FriendlyName, Status, InstanceId -AutoSize | Out-String -Width 240 | Write-Output
} catch {
    Write-Output "(no Bluetooth devices found)"
}

Section "Done"
Write-Output "End of report."
