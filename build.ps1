param(
    [switch]$NoRun,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'NotchWin.csproj'
$outDir = Join-Path $root 'publish'

# 1. Resolve .NET 8 SDK
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# 2. Stop any running Pill or legacy NotchWin instance so publish/ is never locked
foreach ($procName in @('Pill', 'NotchWin')) {
    $running = Get-Process -Name $procName -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Stopping running $procName..." -ForegroundColor Yellow
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 400
    }
}

# 3. Clean old legacy binaries if present
$oldV4 = Join-Path $root 'v4'
if (Test-Path $oldV4) { Remove-Item -Recurse -Force $oldV4 -ErrorAction SilentlyContinue }
foreach ($oldFile in @('NotchWin.exe', 'NotchWin.dll', 'NotchWin.deps.json', 'NotchWin.runtimeconfig.json')) {
    $p = Join-Path $outDir $oldFile
    if (Test-Path $p) { Remove-Item -Force $p -ErrorAction SilentlyContinue }
}

# 4. Publish Release self-contained build
Write-Host 'Building Pill (Release, win-x64)...' -ForegroundColor Cyan
$argsList = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-o', $outDir
)
if (-not $Restore) { $argsList += '--no-restore' }

& $dotnet @argsList
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Build failed!' -ForegroundColor Red
    exit $LASTEXITCODE
}

$exe = Join-Path $outDir 'Pill.exe'
Write-Host "Build succeeded: $exe" -ForegroundColor Green

# 5. Launch immediately unless -NoRun is passed
if (-not $NoRun) {
    Write-Host 'Launching Pill...' -ForegroundColor Cyan
    Start-Process -FilePath $exe
}
