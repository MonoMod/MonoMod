$GitHubBotName=$args[0]
$GitHubBotEmail=$args[1]
$GitHubBotToken=$args[2]

# PowerShell and git don't work well together.
$ErrorActionPreference="Continue"

Write-Output "Setting up config"
$DocFXRepo="https://$GitHubBotToken@github.com/MonoMod/MonoMod.github.io.git"
git config --global user.name $GitHubBotName 2>&1 | ForEach-Object { "$_" }
git config --global user.email $GitHubBotEmail 2>&1 | ForEach-Object { "$_" }

Write-Output "Setting up file hierarchy"
git clone --recursive --branch docfx $DocFXRepo docfx 2>&1 | ForEach-Object { "$_" }
Set-Location docfx
git clone --recursive --branch master $DocFXRepo _site 2>&1 | ForEach-Object { "$_" }

Write-Output "Installing DocFX"
choco install docfx -y

Write-Output "Running docfx build"
docfx metadata
docfx build

Write-Output "Pushing updated _site to master branch on GitHub"
Set-Location _site
git add . 2>&1 | ForEach-Object { "$_" }
git commit --allow-empty -m "Rebuild (automatic commit via Azure Pipelines)" 2>&1 | ForEach-Object { "$_" }
git push 2>&1 | ForEach-Object { "$_" }

Write-Output "Done"
