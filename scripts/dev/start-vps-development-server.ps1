[CmdletBinding()]
param(
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action = 'Start',
    [switch]$Background
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$privateEnvironmentFile = Join-Path $repositoryRoot 'deploy\.env'
$pidFile = Join-Path $PSScriptRoot '.windows-native-dev-server.pid'

function Get-DevEnvironment {
    if (-not (Test-Path -LiteralPath $privateEnvironmentFile)) {
        throw "Missing private configuration: $privateEnvironmentFile"
    }

    foreach ($port in 15434, 18002) {
        if (-not (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)) {
            throw "Required VPS SSH tunnel is not listening on localhost:$port."
        }
    }

    $passwordLine = Get-Content -LiteralPath $privateEnvironmentFile |
        Where-Object { $_ -match '^POSTGRES_PASSWORD=' } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($passwordLine)) {
        throw 'POSTGRES_PASSWORD is missing from deploy/.env.'
    }

    $postgresPassword = $passwordLine.Substring('POSTGRES_PASSWORD='.Length)
    if ([string]::IsNullOrWhiteSpace($postgresPassword)) {
        throw 'POSTGRES_PASSWORD is blank in deploy/.env.'
    }

    return @{
        ASPNETCORE_ENVIRONMENT = 'Development'
        ConnectionStrings__DefaultConnection = "Host=127.0.0.1;Port=15434;Database=savefw_dev;Username=savefw_app;Password=$postgresPassword"
        Valhalla__BaseUrl = 'http://127.0.0.1:18002'
        ArchiveBox__Enabled = 'false'
        TigerSeeding__IngestAddressRanges = 'false'
        DOTNET_WATCH_SUPPRESS_LAUNCH_BROWSER = '1'
    }
}

function Assert-PortAvailable {
    $listener = Get-NetTCPConnection -LocalPort 5000 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($listener) {
        throw 'Port 5000 is already in use. Stop the local development Compose app first: docker compose --env-file deploy/.env --env-file .env.development -f compose.development.yml stop app'
    }
}

function Stop-NativeServer {
    if (-not (Test-Path -LiteralPath $pidFile)) {
        Write-Output 'No native development server pid file exists.'
        return
    }

    $serverPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
    $process = Get-CimInstance Win32_Process -Filter "ProcessId = $serverPid" -ErrorAction SilentlyContinue
    if (-not $process) {
        Remove-Item -LiteralPath $pidFile -Force
        Write-Output 'Removed stale native development server pid file.'
        return
    }
    if ($process.CommandLine -notmatch 'SaveNEIN\.Server\.csproj') {
        throw "Refusing to stop PID $serverPid because it is not this repository's development server."
    }

    Stop-Process -Id $serverPid -Force
    Remove-Item -LiteralPath $pidFile -Force
    Write-Output "Stopped native development server (PID $serverPid)."
}

function Get-NativeServerStatus {
    if (Test-Path -LiteralPath $pidFile) {
        $serverPid = [int](Get-Content -LiteralPath $pidFile -Raw).Trim()
        if (Get-Process -Id $serverPid -ErrorAction SilentlyContinue) {
            Write-Output "Native development server is running (PID $serverPid) at http://localhost:5000."
            return
        }
    }
    Write-Output 'Native development server is not running.'
}

switch ($Action) {
    'Stop' { Stop-NativeServer; return }
    'Status' { Get-NativeServerStatus; return }
}

Assert-PortAvailable
$environment = Get-DevEnvironment
$arguments = 'watch run --project SaveNEIN.Server/SaveNEIN.Server.csproj --urls http://0.0.0.0:5000'

if (-not $Background) {
    foreach ($entry in $environment.GetEnumerator()) {
        Set-Item -Path "Env:$($entry.Key)" -Value $entry.Value
    }
    Set-Location $repositoryRoot
    & dotnet $arguments.Split(' ')
    exit $LASTEXITCODE
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = 'dotnet'
$startInfo.Arguments = $arguments
$startInfo.WorkingDirectory = $repositoryRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
foreach ($entry in $environment.GetEnumerator()) {
    $startInfo.Environment[$entry.Key] = $entry.Value
}

$process = [System.Diagnostics.Process]::Start($startInfo)
$process.Id | Set-Content -LiteralPath $pidFile -NoNewline
Write-Output "Started native development server (PID $($process.Id)) on http://localhost:5000."
