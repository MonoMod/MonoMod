# This script is roughly equivalent to the following Unix command, for each asm file:
#     head -n1 file.asm | sed -e 's/:;//' | sh

pushd $PSScriptRoot

$asm = Get-ChildItem -Recurse -Filter *.asm

foreach ($file in $asm) {
    pushd $file.Directory
    $fst = Get-Content $file -First 1
    $colidx = $fst.IndexOf(":;")
    $cmd = $fst.Substring($colidx + 2).Trim()
    Invoke-Expression $cmd
    popd
}

popd