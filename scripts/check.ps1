#requires -Version 5.1
[CmdletBinding()]
param()

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

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Find-Dotnet
$tests = Join-Path $root 'tests\NeonTetris.Core.Tests'
$project = Join-Path $root 'src\NeonTetris\NeonTetris.csproj'
if (-not (Test-Path -LiteralPath $tests -PathType Container)) {
    throw "Не найден каталог тестов: $tests"
}

Push-Location $root
try {
    Write-Host 'Запуск тестов NeonTetris.Core...'
    & $dotnet test $tests -c Debug
    if ($LASTEXITCODE -ne 0) { throw "Тесты завершились с кодом $LASTEXITCODE." }

    $androidSdk = Find-AndroidSdk
    $javaSdk = Find-JavaSdk
    if (-not $androidSdk -or -not $javaSdk) {
        $missing = @()
        if (-not $androidSdk) { $missing += 'Android SDK (ANDROID_HOME / ANDROID_SDK_ROOT / LocalAppData\Android\Sdk)' }
        if (-not $javaSdk) { $missing += 'Java JDK (JAVA_HOME / C:\Program Files\Android\openjdk)' }
        Write-Warning "Тесты прошли; сборка Android пропущена. Не найдены: $($missing -join '; ')."
        return
    }
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw "Не найден проект Android: $project"
    }

    Write-Host 'Сборка Android Debug...'
    & $dotnet build $project -c Debug -f net10.0-android `
        "-p:AndroidSdkDirectory=$androidSdk" `
        "-p:JavaSdkDirectory=$javaSdk" `
        '-p:ApplicationId=com.mbobka.neondrop'
    if ($LASTEXITCODE -ne 0) { throw "Сборка Android завершилась с кодом $LASTEXITCODE." }
    Write-Host 'Тесты и сборка Android прошли успешно.'
} finally {
    Pop-Location
}
