#!/bin/bash
set -e
trap 'kill 0' EXIT

echo 'Installing Xvfb'
sudo apt install Xvfb

echo 'Starting Xvfb'
Xvfb :0 -screen 0 1024x768x16 &
export DISPLAY=:0.0

echo 'Installing Wine'
sudo dpkg --add-architecture i386
wget -nc https://dl.winehq.org/wine-builds/winehq.key
sudo apt-key add winehq.key
sudo add-apt-repository -y 'deb https://dl.winehq.org/wine-builds/ubuntu/ focal main'
sudo apt install -y --install-recommends winehq-staging

echo 'Booting Wine'
export WINEARCH=win64
export WINEPREFIX=~/.wine
export WINEDEBUG=-all
DISPLAY= wine --version
DISPLAY= wine wineboot

echo 'Installing Wine Mono'
wget -nc https://dl.winehq.org/wine/wine-mono/6.4.0/wine-mono-6.4.0-x86.msi
DISPLAY= wine msiexec /i wine-mono-6.4.0-x86.msi /quiet

echo 'Installing PowerShell Core'
wget -nc https://github.com/PowerShell/PowerShell/releases/download/v7.1.4/PowerShell-7.1.4-win-x64.msi
DISPLAY= wine msiexec /i PowerShell-7.1.4-win-x64.msi /quiet
wine pwsh -c 'echo $PSVersionTable'

echo 'Installing dotnet'
wget -nc https://dotnet.microsoft.com/download/dotnet/scripts/v1/dotnet-install.ps1
wine pwsh dotnet-install.ps1 -Channel release/5.0.1xx
wine pwsh dotnet-install.ps1 -Channel release/3.1.1xx
wine pwsh dotnet-install.ps1 -Channel release/3.0.1xx
wine pwsh dotnet-install.ps1 -Channel release/2.1.1xx

echo 'Building and testing'
wine dotnet build --configuration $BUILD_CONFIGURATION
wine dotnet test --configuration $BUILD_CONFIGURATION -l:"trx;LogFileName=testresults.wine.linux.trx"
