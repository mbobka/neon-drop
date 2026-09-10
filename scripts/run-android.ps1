#requires -Version 5.1
[CmdletBinding(DefaultParameterSetName = 'Auto')]
param(
    [Parameter(ParameterSetName = 'Avd')]
    [ValidateNotNullOrEmpty()]
    [string]$Avd,
    [Parameter(ParameterSetName = 'Serial')]
    [ValidateNotNullOrEmpty()]
    [string]$Serial,
    [ValidateRange(1, 3600)]
    [int]$BootTimeoutSeconds = 180
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# Коды завершения внешних программ проверяются явно, в том числе в PowerShell 7.
$PSNativeCommandUseErrorActionPreference = $false

function Find-Dotnet {
    $command = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($command) { return $command.Source }

    foreach ($base in @($env:DOTNET_ROOT, "${env:ProgramFiles}\dotnet", "$env:LOCALAPPDATA\Microsoft\dotnet", "$env:USERPROFILE\.dotnet")) {
        if ($base -and (Test-Path -LiteralPath (Join-Path $base 'dotnet.exe') -PathType Leaf)) {
            return (Join-Path $base 'dotnet.exe')
        }
    }
    throw 'Не найден dotnet.exe. Установите .NET 10 SDK или добавьте dotnet в PATH.'
}

function Find-AndroidSdk {
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "$env:LOCALAPPDATA\Android\Sdk")) {
        if ($candidate -and (Test-Path -LiteralPath (Join-Path $candidate 'platform-tools\adb.exe') -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Find-JavaSdk {
    $candidates = @($env:JAVA_HOME)
    $openJdkRoot = 'C:\Program Files\Android\openjdk'
    if (Test-Path -LiteralPath $openJdkRoot -PathType Container) {
        $candidates += $openJdkRoot
        $candidates += @(Get-ChildItem -LiteralPath $openJdkRoot -Directory |
            Sort-Object Name -Descending | Select-Object -ExpandProperty FullName)
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and
            (Test-Path -LiteralPath (Join-Path $candidate 'bin\java.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $candidate 'bin\javac.exe') -PathType Leaf)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

$script:bootWatch = $null

function Invoke-Adb {
    param([string[]]$Arguments)
    # Ограничиваем и сам adb: ожидание устройства не должно зависнуть внутри команды.
    $timeoutMs = 120000
    if ($script:bootWatch -and $script:bootWatch.IsRunning) {
        $timeoutMs = [int][Math]::Max(1, [Math]::Min($timeoutMs,
            $BootTimeoutSeconds * 1000 - $script:bootWatch.ElapsedMilliseconds))
    }
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $script:adb
    # Экранирование аргументов Windows, включая пробелы и завершающие обратные слеши.
    $startInfo.Arguments = (@($Arguments | ForEach-Object {
        '"' + [regex]::Replace([regex]::Replace($_, '(\\*)"', '$1$1\"'), '(\\+)$', '$1$1') + '"'
    }) -join ' ')
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        $null = $process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($timeoutMs)) {
            $process.Kill()
            throw "Превышено время ожидания adb ($($Arguments -join ' '))."
        }
        $output = $stdout.GetAwaiter().GetResult() + [Environment]::NewLine + $stderr.GetAwaiter().GetResult()
        $lines = @($output -split '\r?\n' | Where-Object { $_ })
        return [pscustomobject]@{ Lines = $lines; ExitCode = $process.ExitCode }
    } finally {
        $process.Dispose()
    }
}

function Get-Devices {
    $result = Invoke-Adb -Arguments @('devices')
    if ($result.ExitCode -ne 0) {
        throw "Не удалось получить список устройств adb: $($result.Lines -join [Environment]::NewLine)"
    }
    foreach ($line in $result.Lines) {
        if ($line -match '^(\S+)\s+(device|offline|unauthorized)\s*$') {
            [pscustomobject]@{ Serial = $Matches[1]; State = $Matches[2] }
        }
    }
}

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\NeonTetris\NeonTetris.csproj'
$appId = 'com.mbobka.neondrop'
$dotnet = Find-Dotnet
$androidSdk = Find-AndroidSdk
$javaSdk = Find-JavaSdk
if (-not $androidSdk) {
    throw 'Не найден Android SDK с platform-tools\adb.exe. Задайте ANDROID_HOME или ANDROID_SDK_ROOT.'
}
if (-not $javaSdk) {
    throw 'Не найден Java JDK. Задайте JAVA_HOME или установите JDK в C:\Program Files\Android\openjdk.'
}
if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
    throw "Не найден проект: $project"
}

$script:adb = Join-Path $androidSdk 'platform-tools\adb.exe'
$devices = @(Get-Devices)
$deviceSerial = $null
$emulatorProcess = $null
$selectedAvd = $null
if ($Serial) {
    if (-not ($devices | Where-Object { $_.Serial -eq $Serial })) {
        throw "Устройство '$Serial' не найдено в adb devices."
    }
    $deviceSerial = $Serial
} elseif (-not $Avd) {
    # При явном -Avd выбираем эмулятор по имени ниже, даже если другие устройства уже готовы.
    $readyDevices = @($devices | Where-Object { $_.State -eq 'device' } | Sort-Object Serial)
    if ($readyDevices.Count -gt 0) {
        $deviceSerial = $readyDevices[0].Serial
        if ($readyDevices.Count -gt 1) {
            Write-Host "Найдено несколько устройств; выбрано $deviceSerial. Для выбора используйте -Serial."
        }
    }
}

$bootWatch = [Diagnostics.Stopwatch]::StartNew()
if (-not $deviceSerial) {
    $emulator = Join-Path $androidSdk 'emulator\emulator.exe'
    if (-not (Test-Path -LiteralPath $emulator -PathType Leaf)) {
        throw 'Нет доступного устройства и emulator.exe. Подключите устройство или установите Android Emulator.'
    }
    $avds = @(& $emulator -list-avds | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось получить список AVD.' }
    if ($avds.Count -eq 0) { throw 'Нет AVD. Создайте виртуальное устройство в Android Device Manager.' }
    $selectedAvd = if ($Avd) { $Avd } else { $avds[0] }
    if ($selectedAvd -notin $avds) {
        throw "AVD '$selectedAvd' не найден. Доступны: $($avds -join ', ')."
    }
    # Имена AVD не содержат пробелов или кавычек; проверка защищает передачу аргументов Start-Process.
    if ($selectedAvd -notmatch '^[\w.-]+$') { throw "Недопустимое имя AVD: $selectedAvd" }

    # Повторный запуск скрипта может застать уже загружающийся эмулятор.
    foreach ($device in $devices | Where-Object { $_.Serial -like 'emulator-*' }) {
        $name = Invoke-Adb -Arguments @('-s', $device.Serial, 'emu', 'avd', 'name')
        if ($name.ExitCode -eq 0 -and $selectedAvd -in $name.Lines) {
            $deviceSerial = $device.Serial
            break
        }
    }
    if (-not $deviceSerial) {
        Write-Host "Запуск AVD: $selectedAvd"
        $emulatorProcess = Start-Process -FilePath $emulator -ArgumentList @('-avd', $selectedAvd) -WindowStyle Hidden -PassThru
    }
}

Write-Host "Ожидание sys.boot_completed=1 (до $BootTimeoutSeconds с)..."
$booted = $false
while ($bootWatch.Elapsed.TotalSeconds -lt $BootTimeoutSeconds) {
    if (-not $deviceSerial) {
        foreach ($device in @(Get-Devices) | Where-Object { $_.Serial -like 'emulator-*' -and $_.State -eq 'device' }) {
            $name = Invoke-Adb -Arguments @('-s', $device.Serial, 'emu', 'avd', 'name')
            if ($name.ExitCode -eq 0 -and $selectedAvd -in $name.Lines) {
                $deviceSerial = $device.Serial
                break
            }
        }
    }
    if ($deviceSerial) {
        $boot = Invoke-Adb -Arguments @('-s', $deviceSerial, 'shell', 'getprop', 'sys.boot_completed')
        if ($boot.ExitCode -eq 0 -and ($boot.Lines -join '').Trim() -eq '1') {
            $booted = $true
            break
        }
    }
    if ($emulatorProcess -and $emulatorProcess.HasExited -and -not $deviceSerial) {
        throw "Эмулятор завершился до появления устройства (код $($emulatorProcess.ExitCode))."
    }
    Start-Sleep -Seconds 2
}
$bootWatch.Stop()
if (-not $booted) {
    $state = @(Get-Devices) | ForEach-Object { "$($_.Serial): $($_.State)" }
    throw "Устройство не загрузилось за $BootTimeoutSeconds с. adb: $($state -join ', '). Для unauthorized подтвердите USB-отладку."
}
Write-Host "Устройство готово: $deviceSerial"

# Отдельный каталог исключает выбор APK из чужой сборки или другой конфигурации.
$apkDirectory = Join-Path $root 'src\NeonTetris\bin\run-android'
$buildArguments = @(
    'build', $project, '-c', 'Debug', '-f', 'net10.0-android',
    "-p:AndroidSdkDirectory=$androidSdk",
    "-p:JavaSdkDirectory=$javaSdk",
    "-p:ApplicationId=$appId",
    '-p:AndroidPackageFormats=apk',
    '-p:AndroidBuildApplicationPackage=true',
    '-p:EmbedAssembliesIntoApk=true',
    '-o', $apkDirectory
)
Push-Location $root
try {
    & $dotnet @buildArguments
    if ($LASTEXITCODE -ne 0) { throw "Сборка Android завершилась с кодом $LASTEXITCODE." }
} finally {
    Pop-Location
}

$apk = Join-Path $apkDirectory "$appId-Signed.apk"
if (-not (Test-Path -LiteralPath $apk -PathType Leaf)) {
    throw "Подписанный APK не найден: $apk"
}
Write-Host "Установка: $apk"
$install = Invoke-Adb -Arguments @('-s', $deviceSerial, 'install', '-r', $apk)
$install.Lines | ForEach-Object { Write-Host $_ }
if ($install.ExitCode -ne 0 -or -not ($install.Lines -match '^Success\s*$')) {
    throw 'Не удалось установить APK. Проверьте сообщение adb выше.'
}

# Имя Activity вычисляется Android: хеш crc в имени MAUI Activity может меняться.
$resolved = Invoke-Adb -Arguments @(
    '-s', $deviceSerial, 'shell', 'cmd', 'package', 'resolve-activity', '--brief',
    '-a', 'android.intent.action.MAIN', '-c', 'android.intent.category.LAUNCHER', $appId
)
$component = $resolved.Lines | Where-Object {
    $_ -match ('^' + [regex]::Escape($appId) + '/[\w.$]+$')
} | Select-Object -Last 1
if ($resolved.ExitCode -eq 0 -and $component) {
    $launch = Invoke-Adb -Arguments @('-s', $deviceSerial, 'shell', 'am', 'start', '-W', '-n', $component)
    $launch.Lines | ForEach-Object { Write-Host $_ }
    if ($launch.ExitCode -ne 0 -or ($launch.Lines -match '(^Error|Exception|Status:\s*(timeout|error))')) {
        throw 'Android не смог запустить Activity. Проверьте сообщение выше.'
    }
} else {
    $launch = Invoke-Adb -Arguments @(
        '-s', $deviceSerial, 'shell', 'monkey', '-p', $appId,
        '-c', 'android.intent.category.LAUNCHER', '1'
    )
    $launch.Lines | ForEach-Object { Write-Host $_ }
    if ($launch.ExitCode -ne 0 -or -not ($launch.Lines -match 'Events injected:\s*1')) {
        throw 'Не удалось запустить приложение через monkey.'
    }
}
Write-Host "Запущено $appId на $deviceSerial."
