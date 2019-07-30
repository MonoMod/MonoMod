$DocFXRepo="git@github.com:MonoMod/MonoMod.github.io.git"

Write-Output "Installing DocFX"
choco install docfx -y

Write-Output "Setting up file hierarchy"
git clone --recursive --branch docfx $DocFXRepo docfx
Set-Location docfx
git clone --recursive --branch master $DocFXRepo _site

Write-Output "Running docfx build"
docfx build

Write-Output "Pushing updated _site to master branch on GitHub"
Set-Location _site
git add .
git commit -m 'Rebuild (automatic commit via Azure Pipelines)'
git push

Write-Output "Done"
