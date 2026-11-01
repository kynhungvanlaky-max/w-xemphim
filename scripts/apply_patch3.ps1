$file = "src/Server/WebXemPhim.Server/Data/DatabaseSchemaHelper.cs"
$lines = [System.IO.File]::ReadAllLines($file)
$newBlock = [System.IO.File]::ReadAllLines("scripts/new_block.txt")
$helper = [System.IO.File]::ReadAllLines("scripts/helper.txt")

$start = -1
$end = -1
for ($i = 0; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '^\s+else\s*$' -and $lines[$i+1] -match '^\s+\{\s*$' -and $lines[$i+2] -match 'IX_mfa_challenges_UserId') {
        $start = $i
        break
    }
}

if ($start -ge 0) {
    for ($i = $start + 2; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^\s+\}\s*$') {
            $end = $i
            break
        }
    }
}

Write-Host "Replacing lines $start to $end"

$list = [System.Collections.Generic.List[string]]::new()
for ($i = 0; $i -lt $start; $i++) {
    $list.Add($lines[$i])
}
foreach ($l in $newBlock) {
    $list.Add($l)
}
for ($i = $end + 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -like "*Task<bool> CheckTableExistsAsync*") {
        foreach ($h in $helper) {
            $list.Add($h)
        }
        $list.Add("")
    }
    $list.Add($lines[$i])
}

[System.IO.File]::WriteAllLines($file, $list.ToArray(), [System.Text.Encoding]::UTF8)
Write-Host "PATCH_COMPLETE"
