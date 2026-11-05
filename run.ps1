# Запуск детектора на Windows. При первом запуске сам ставит .NET SDK 10 (без прав администратора) и собирает программу.
# Обычно запускается через run.cmd:
#   run.cmd https://gitlab.company.com/group/project/-/merge_requests/123
#   run.cmd web
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Test-Sdk([string]$exe) {
    try {
        $sdks = & $exe --list-sdks 2>$null
        return [bool]($sdks | Where-Object { $_ -match '^(1[0-9])\.' })
    } catch {
        return $false
    }
}

$localDir = Join-Path (Join-Path $env:LOCALAPPDATA 'Microsoft') 'dotnet'
$localExe = Join-Path $localDir $(if ($IsLinux -or $IsMacOS) { 'dotnet' } else { 'dotnet.exe' })
$dotnet = $null
$cmd = Get-Command dotnet -ErrorAction SilentlyContinue
if ($cmd -and (Test-Sdk $cmd.Source)) {
    $dotnet = $cmd.Source
} elseif ((Test-Path $localExe) -and (Test-Sdk $localExe)) {
    $dotnet = $localExe
} else {
    Write-Host 'Устанавливаю .NET SDK 10 в' $localDir '(один раз, без прав администратора)...'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Channel 10.0 -InstallDir $localDir
    $dotnet = $localExe
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
if ($dotnet -eq $localExe) { $env:DOTNET_ROOT = $localDir }

$project = Join-Path (Join-Path $root 'src') 'AiDetect.Cli'
$out = Join-Path $root '.build'
$bin = Join-Path $out 'aidetect.dll'
$needBuild = -not (Test-Path $bin)
if (-not $needBuild) {
    $built = (Get-Item $bin).LastWriteTime
    $sources = @(Join-Path $root 'src'; Join-Path $root 'model') | Where-Object { Test-Path $_ }
    $newer = Get-ChildItem -Path $sources -Recurse -File -Include *.cs, *.csproj, *.json |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.LastWriteTime -gt $built } |
        Select-Object -First 1
    $needBuild = [bool]$newer
}

if ($needBuild) {
    Write-Host 'Сборка...'
    & $dotnet build $project -c Release -o $out --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & $dotnet build $project -c Release -o $out --nologo
        exit 1
    }
}

& $dotnet $bin @args
exit $LASTEXITCODE
