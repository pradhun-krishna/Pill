param(
    [switch]$TestSign,
    [switch]$Restore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'NotchWin.csproj'
$cfgPath = Join-Path $root 'store-config.json'
$stageDir = Join-Path $root 'msix-stage'
$distDir = Join-Path $root 'dist'

if (-not (Test-Path $cfgPath)) {
    throw "Missing store-config.json at $cfgPath"
}
$cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json

# 1. Locate .NET 8 SDK
$dotnet = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

# 2. Locate makeappx.exe (local .sdk-tools, Windows Kits, or NuGet cache)
$toolsDir = Join-Path $root '.sdk-tools'

function Find-SdkTool([string]$exeName) {
    $cmd = Get-Command $exeName -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $searchRoots = @(
        $toolsDir,
        'C:\Program Files (x86)\Windows Kits\10\bin',
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools')
    )
    foreach ($sr in $searchRoots) {
        if (Test-Path $sr) {
            $found = Get-ChildItem $sr -Recurse -Filter $exeName -ErrorAction SilentlyContinue |
                     Where-Object { $_.FullName -match '\\x64\\' } |
                     Sort-Object FullName -Descending |
                     Select-Object -First 1
            if ($found) { return $found.FullName }
        }
    }
    return $null
}

$makeappx = Find-SdkTool 'makeappx.exe'
if (-not $makeappx) {
    Write-Host 'Downloading official Microsoft.Windows.SDK.BuildTools (makeappx.exe, no admin needed)...' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    $nupkg = Join-Path $toolsDir 'sdk-buildtools.zip'
    $url = 'https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/10.0.22621.3233'
    Invoke-WebRequest -Uri $url -OutFile $nupkg -UseBasicParsing
    Expand-Archive -Path $nupkg -DestinationPath $toolsDir -Force
    Remove-Item -Force $nupkg -ErrorAction SilentlyContinue
    $makeappx = Find-SdkTool 'makeappx.exe'
    if (-not $makeappx) {
        throw 'Failed to locate makeappx.exe after extracting Microsoft.Windows.SDK.BuildTools.'
    }
    Write-Host "SDK tools ready: $makeappx" -ForegroundColor Green
}

# 3. Clean staging directory and publish Release x64 self-contained build
if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir }
New-Item -ItemType Directory -Force -Path $stageDir, $distDir | Out-Null

Write-Host "Publishing Pill v$($cfg.Version) (Release, win-x64)..." -ForegroundColor Cyan
$pubArgs = @(
    'publish', $proj,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-o', $stageDir
)
if (-not $Restore) { $pubArgs += '--no-restore' }
& $dotnet @pubArgs
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed!' }

# Remove debug symbols (.pdb) and unused non-English .NET satellite folders to keep MSIX lean & single-PRI
Get-ChildItem $stageDir -Filter '*.pdb' | Remove-Item -Force -ErrorAction SilentlyContinue
foreach ($lang in @('cs','de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant')) {
    $ld = Join-Path $stageDir $lang
    if (Test-Path $ld) { Remove-Item -Recurse -Force $ld -ErrorAction SilentlyContinue }
}

# 4. Copy MSIX visual assets and stamp AppxManifest.xml
$assetsOut = Join-Path $stageDir 'Assets'
New-Item -ItemType Directory -Force -Path $assetsOut | Out-Null
Copy-Item (Join-Path $root 'Package\Assets\*.png') $assetsOut -Force
Remove-Item (Join-Path $assetsOut 'StoreListing_300x300.png') -Force -ErrorAction SilentlyContinue

$manifestTpl = Get-Content (Join-Path $root 'Package\AppxManifest.xml') -Raw
$manifestXml = $manifestTpl `
    -replace '__DISPLAY_NAME__', $cfg.DisplayName `
    -replace '__PACKAGE_NAME__', $cfg.PackageName `
    -replace '__PUBLISHER__', $cfg.Publisher `
    -replace '__PUBLISHER_DISPLAY_NAME__', $cfg.PublisherDisplayName `
    -replace '__VERSION__', $cfg.Version
[System.IO.File]::WriteAllText((Join-Path $stageDir 'AppxManifest.xml'), $manifestXml, [System.Text.UTF8Encoding]::new($false))

# 5. Generate ONE unified resources.pri (no split .scale-200.pri / .language-*.pri files, which Partner Center rejects in standalone .msix!)
$makepri = Find-SdkTool 'makepri.exe'
if ($makepri) {
    $priCfg = Join-Path $root 'msix-priconfig.xml'
    $priOut = Join-Path $stageDir 'resources.pri'
    & $makepri createconfig /cf $priCfg /dq en-US /o | Out-Null
    [xml]$xml = Get-Content $priCfg
    $pkgNode = $xml.resources.packaging
    if ($pkgNode) { [void]$xml.resources.RemoveChild($pkgNode) }
    $xml.Save($priCfg)
    & $makepri new /pr $stageDir /cf $priCfg /mn (Join-Path $stageDir 'AppxManifest.xml') /of $priOut /o | Out-Null
    Remove-Item -Force $priCfg -ErrorAction SilentlyContinue
}

# 6. Pack MSIX
$msixPath = Join-Path $distDir "Pill_$($cfg.Version)_x64.msix"
if (Test-Path $msixPath) { Remove-Item -Force $msixPath }

Write-Host "Packing MSIX -> $msixPath" -ForegroundColor Cyan
& $makeappx pack /d $stageDir /p $msixPath /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed!' }

# 6. Optional local self-signed certificate for sideload testing before Partner Center upload
if ($TestSign) {
    $signtool = Find-SdkTool 'signtool.exe'
    if (-not $signtool) { throw 'signtool.exe not found for -TestSign.' }

    $cert = Get-ChildItem Cert:\CurrentUser\My |
            Where-Object { $_.Subject -eq $cfg.Publisher -and $_.HasPrivateKey } |
            Select-Object -First 1
    if (-not $cert) {
        Write-Host "Creating local test certificate for $($cfg.Publisher)..." -ForegroundColor Yellow
        $cert = New-SelfSignedCertificate -Type Custom -Subject $cfg.Publisher `
            -KeyUsage DigitalSignature -FriendlyName 'Pill Local Test Cert' `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        Write-Host "NOTE: Trust this cert in LocalMachine\TrustedPeople once to sideload locally." -ForegroundColor Yellow
    }
    & $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msixPath
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed!' }
}

Write-Host ''
Write-Host "MSIX READY: $msixPath" -ForegroundColor Green
if ($cfg.Publisher -like '*REPLACE*') {
    Write-Host 'Reminder: Replace Publisher & PackageName in store-config.json with your exact Partner Center values before uploading!' -ForegroundColor Yellow
}
