$changed = git diff --cached --name-only

$implementationChanged =
    $changed | Where-Object {
        $_ -match '^(src|tests|infra)/'
    }

$currentStateChanged =
    $changed -contains 'docs/04_CURRENT_STATE.md'

if ($implementationChanged -and -not $currentStateChanged)
{
    Write-Warning @"
Implementation changed, but docs/04_CURRENT_STATE.md was not modified.
Check whether CURRENT_STATE needs to be updated.
"@
}