$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceProjectDir = Join-Path $projectDir 'CONTROL TAXI'
$listenUrl = if ([string]::IsNullOrWhiteSpace($env:CONTROLTAXI_URLS)) { 'http://0.0.0.0:5298' } else { $env:CONTROLTAXI_URLS }
$browserUrl = if ([string]::IsNullOrWhiteSpace($env:CONTROLTAXI_BROWSER_URL)) { 'http://localhost:5298' } else { $env:CONTROLTAXI_BROWSER_URL }
$port = 5298
$outLog = Join-Path $projectDir 'controltaxi-run.out.log'
$errLog = Join-Path $projectDir 'controltaxi-run.err.log'

function Get-DotNetPath {
    $candidates = @(
        'C:\Program Files\dotnet\dotnet.exe',
        'C:\Program Files (x86)\dotnet\dotnet.exe',
        (Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe')
    )

    foreach ($candidate in $candidates) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }

    $command = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($command -and (Test-Path -LiteralPath $command.Source)) {
        return $command.Source
    }

    throw "No encontre dotnet.exe. Instala .NET o revisa que exista en C:\Program Files\dotnet\dotnet.exe"
}

function Test-ControlTaxiPort {
    $client = $null
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $async = $client.BeginConnect('127.0.0.1', $port, $null, $null)
        if (-not $async.AsyncWaitHandle.WaitOne(1000)) {
            return $false
        }

        $client.EndConnect($async)
        return $true
    }
    catch {
        return $false
    }
    finally {
        if ($client) {
            $client.Close()
        }
    }
}

try {
    if (-not (Test-ControlTaxiPort)) {
        $publishedExe = Join-Path $projectDir 'ControlTaxiWeb.exe'
        $publishedDll = Join-Path $projectDir 'ControlTaxiWeb.dll'
        $projectFile = Join-Path $projectDir 'ControlTaxiWeb.csproj'
        $sourceProjectFile = Join-Path $sourceProjectDir 'ControlTaxiWeb.csproj'

        if (Test-Path $publishedExe) {
            Start-Process -FilePath $publishedExe `
                -ArgumentList @('--urls', $listenUrl) `
                -WorkingDirectory $projectDir `
                -WindowStyle Hidden `
                -RedirectStandardOutput $outLog `
                -RedirectStandardError $errLog
        }
        elseif (Test-Path $publishedDll) {
            $dotnetPath = Get-DotNetPath
            Start-Process -FilePath $dotnetPath `
                -ArgumentList @('ControlTaxiWeb.dll', '--urls', $listenUrl) `
                -WorkingDirectory $projectDir `
                -WindowStyle Hidden `
                -RedirectStandardOutput $outLog `
                -RedirectStandardError $errLog
        }
        elseif (Test-Path $sourceProjectFile) {
            Start-Process -FilePath 'dotnet' `
                -ArgumentList @('run', '--no-launch-profile', '--project', 'ControlTaxiWeb.csproj', '--urls', $listenUrl) `
                -WorkingDirectory $sourceProjectDir `
                -WindowStyle Hidden `
                -RedirectStandardOutput $outLog `
                -RedirectStandardError $errLog
        }
        elseif (Test-Path $projectFile) {
            Start-Process -FilePath 'dotnet' `
                -ArgumentList @('run', '--no-launch-profile', '--project', 'ControlTaxiWeb.csproj', '--urls', $listenUrl) `
                -WorkingDirectory $projectDir `
                -WindowStyle Hidden `
                -RedirectStandardOutput $outLog `
                -RedirectStandardError $errLog
        }
        else {
            throw "No encontre ControlTaxiWeb.exe, ControlTaxiWeb.dll ni ControlTaxiWeb.csproj en: $projectDir"
        }

        $started = $false
        for ($i = 0; $i -lt 12; $i++) {
            Start-Sleep -Milliseconds 500
            if (Test-ControlTaxiPort) {
                $started = $true
                break
            }
        }
    }
    else {
        $started = $true
    }

    if (-not $started) {
        $message = "Control Taxi no pudo iniciar en $listenUrl.`n`nRevisa este archivo:`n$errLog"
        if (Test-Path $errLog) {
            $errorText = Get-Content $errLog -Raw -ErrorAction SilentlyContinue
            if (-not [string]::IsNullOrWhiteSpace($errorText)) {
                $message += "`n`nError:`n$errorText"
            }
        }
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show($message, 'Control Taxi') | Out-Null
        exit 1
    }

    Start-Process $browserUrl
    exit 0
}
catch {
    Add-Type -AssemblyName PresentationFramework
    [System.Windows.MessageBox]::Show($_.Exception.Message, 'Control Taxi') | Out-Null
    exit 1
}
