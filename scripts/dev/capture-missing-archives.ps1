$ErrorActionPreference = 'Stop'

$config = Get-Content -Raw "$PSScriptRoot\..\..\SaveNEIN.Server\appsettings.json" | ConvertFrom-Json
$tokenLine = Get-Content "$PSScriptRoot\..\..\deploy\.env" |
    Where-Object { $_ -match '^ARCHIVEBOX_CAPTURE_ADMIN_TOKEN=' } |
    Select-Object -First 1

if ($null -eq $tokenLine) {
    throw 'ARCHIVEBOX_CAPTURE_ADMIN_TOKEN is not configured.'
}

$captureToken = $tokenLine.Substring('ARCHIVEBOX_CAPTURE_ADMIN_TOKEN='.Length).Trim('"', "'")

foreach ($source in $config.ArchiveBox.Sources) {
    $latestStatus = & curl.exe --silent --output NUL --write-out '%{http_code}' "https://savenein.com/api/web-archives/$($source.Key)/latest"
    if ($latestStatus -eq '200') {
        Write-Output "$($source.Key)`tEXISTS"
        continue
    }

    Write-Output "$($source.Key)`tCAPTURING"
    $captureStatus = & curl.exe --silent --show-error --output NUL --write-out '%{http_code}' --max-time 700 --request POST --header "X-Archive-Capture-Token: $captureToken" "https://savenein.com/api/web-archives/capture/$($source.Key)"
    Write-Output "$($source.Key)`t$captureStatus"
}
