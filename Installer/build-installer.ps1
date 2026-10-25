param(
    [string]$IsccPath,
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '1.0.0',
    [switch]$DownloadCompiler
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish\win-x64'
$output = Join-Path $root 'artifacts\installer'

if (-not $IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
}

if (-not $IsccPath) {
    foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
        if (-not $base) { continue }
        $candidate = Join-Path $base 'Inno Setup 6\ISCC.exe'
        if (Test-Path $candidate) { $IsccPath = $candidate; break }
    }
}

if (-not $IsccPath -and $DownloadCompiler) {
    $compilerVersion = '6.7.3'
    $tools = Join-Path $root "artifacts\tools\innosetup-$compilerVersion"
    $baseUrl = "https://api.nuget.org/v3-flatcontainer/tools.innosetup/$compilerVersion/tools.innosetup.$compilerVersion.nupkg"
    New-Item -ItemType Directory -Path $tools -Force | Out-Null
    $archive = Join-Path $tools 'compiler.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $baseUrl -OutFile $archive -UseBasicParsing
    $registration = Invoke-RestMethod -Uri "https://api.nuget.org/v3/registration5-gz-semver2/tools.innosetup/$compilerVersion.json"
    $metadata = Invoke-RestMethod -Uri $registration.catalogEntry
    if ($metadata.packageHashAlgorithm -ne 'SHA512') { throw 'Algoritmo de hash inesperado no catalogo NuGet.' }
    $expected = $metadata.packageHash
    $sha512 = [Security.Cryptography.SHA512]::Create()
    try {
        $actual = [Convert]::ToBase64String($sha512.ComputeHash([IO.File]::ReadAllBytes($archive)))
    } finally {
        $sha512.Dispose()
    }
    if ($actual -cne $expected) { throw 'Falha ao verificar o hash SHA-512 do compilador Inno Setup.' }
    Expand-Archive -LiteralPath $archive -DestinationPath $tools -Force
    $IsccPath = (Get-ChildItem -LiteralPath $tools -Filter ISCC.exe -Recurse | Select-Object -First 1).FullName
}

if (-not $IsccPath -or -not (Test-Path $IsccPath -PathType Leaf)) {
    throw 'Instale Inno Setup 6, informe -IsccPath ou execute com -DownloadCompiler para obter o compilador portatil do NuGet.'
}

& dotnet publish (Join-Path $root 'MarcelinoClient\MarcelinoClient.csproj') -p:PublishProfile=WindowsInstaller -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Falha ao publicar a aplicacao.' }

& $IsccPath "/DAppVersion=$Version" "/DPublishDir=$publish" "/DInstallerOutput=$output" (Join-Path $PSScriptRoot 'MarcelinoClient.iss')
if ($LASTEXITCODE -ne 0) { throw 'Falha ao gerar o instalador.' }

Write-Output (Join-Path $output "MarcelinoClient-Setup-$Version-win-x64.exe")