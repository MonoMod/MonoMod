if ($(git rev-parse "@:MonoMod.Common") -eq $(git rev-parse "@~:MonoMod.Common")) {
    Write-Output "MonoMod.Common wasn't changed."
    for ($i = 0; $i -lt $args.Length; $i++) {
        Write-Output "Deleting $($args[$i])"
        Remove-Item -Path "$($args[$i])"
    }
} else {
    Write-Output "MonoMod.Common was changed."
}
