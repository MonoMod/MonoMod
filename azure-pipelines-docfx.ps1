$DocFXRepo="git@github.com:MonoMod/MonoMod.github.io.git"

Write-Output "Setting up file hierarchy"
git clone --recursive --branch docfx $DocFXRepo docfx 2>&1
Set-Location docfx
git clone --recursive --branch master $DocFXRepo _site 2>&1

Write-Output "Installing DocFX"
choco install docfx -y

Write-Output "Running docfx build"
docfx build

Write-Output "Pushing updated _site to master branch on GitHub"
Set-Location _site
git add . 2>&1
git commit -m 'Rebuild (automatic commit via Azure Pipelines)' 2>&1
git push 2>&1

Write-Output "Done"
