$ErrorActionPreference = "Stop"

$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$Version = "0.0.0"
$RuntimeId = "win-x64"
$ReleaseRoot = Join-Path $RepositoryRoot "artifacts/release"
$StagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("catnip-windows-package-" + [Guid]::NewGuid().ToString("N"))
$PackageRoot = Join-Path $StagingRoot "Catnip-$Version-$RuntimeId"
$PayloadZip = Join-Path $StagingRoot "Catnip-$Version-$RuntimeId.zip"
$BootstrapperRoot = Join-Path $StagingRoot "bootstrapper"

try {
    New-Item -ItemType Directory -Force -Path $PackageRoot | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "DemoApi") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "Runtime") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PackageRoot "WorkBuddyBridge") | Out-Null
    New-Item -ItemType Directory -Force -Path $ReleaseRoot | Out-Null

    dotnet publish (Join-Path $RepositoryRoot "src/Catnip.Desktop/Catnip.Desktop.csproj") -c Release -r $RuntimeId --self-contained true -p:UseAppHost=true -o $PackageRoot
    dotnet publish (Join-Path $RepositoryRoot "src/Catnip.DemoApi/Catnip.DemoApi.csproj") -c Release -r $RuntimeId --self-contained true -p:UseAppHost=true -o (Join-Path $PackageRoot "DemoApi")
    dotnet publish (Join-Path $RepositoryRoot "src/Catnip.Runtime/Catnip.Runtime.csproj") -c Release -r $RuntimeId --self-contained true -p:UseAppHost=true -o (Join-Path $PackageRoot "Runtime")
    dotnet publish (Join-Path $RepositoryRoot "src/Catnip.WorkBuddyBridge/Catnip.WorkBuddyBridge.csproj") -c Release -r $RuntimeId --self-contained true -p:UseAppHost=true -o (Join-Path $PackageRoot "WorkBuddyBridge")

    Copy-Item -LiteralPath (Join-Path $RepositoryRoot "packaging/windows/README-WINDOWS.md") -Destination (Join-Path $PackageRoot "README-WINDOWS.md")
    Compress-Archive -Path (Join-Path $PackageRoot "*") -DestinationPath $PayloadZip -CompressionLevel Optimal

    dotnet publish (Join-Path $RepositoryRoot "packaging/windows/bootstrapper/Catnip.WindowsBootstrapper.csproj") -c Release -r $RuntimeId --self-contained true -p:PublishSingleFile=true "-p:PayloadZipPath=$PayloadZip" -o $BootstrapperRoot

    $ExecutableName = "Catnip-$Version-$RuntimeId.exe"
    Copy-Item -LiteralPath (Join-Path $BootstrapperRoot $ExecutableName) -Destination (Join-Path $ReleaseRoot $ExecutableName) -Force
    Copy-Item -LiteralPath (Join-Path $BootstrapperRoot $ExecutableName) -Destination (Join-Path $ReleaseRoot "catnip.exe") -Force
    Copy-Item -LiteralPath $PayloadZip -Destination (Join-Path $ReleaseRoot "Catnip-$Version-$RuntimeId.zip") -Force

    Write-Output (Join-Path $ReleaseRoot $ExecutableName)
    Write-Output (Join-Path $ReleaseRoot "catnip.exe")
    Write-Output (Join-Path $ReleaseRoot "Catnip-$Version-$RuntimeId.zip")
}
finally {
    if (Test-Path -LiteralPath $StagingRoot) {
        Remove-Item -LiteralPath $StagingRoot -Recurse -Force
    }
}
