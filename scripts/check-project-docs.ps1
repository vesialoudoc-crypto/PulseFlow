$changed = git diff --cached --name-only
$added = git diff --cached --name-only --diff-filter=A

$implementationChanged =
    $changed | Where-Object {
        $_ -match '^(src|tests|infra)/'
    }

$checkpointAdded =
    $added | Where-Object {
        $_ -match '^docs/progress/\d{4}-\d{2}-\d{2}-\d{3}-[a-z0-9]+(?:-[a-z0-9]+)*\.md$'
    }

if ($implementationChanged -and -not $checkpointAdded)
{
    Write-Warning @"
Implementation changed, but no new checkpoint was added to docs/progress/.
Check whether the staged changes form a meaningful milestone that needs a checkpoint.
"@
}

$historicalCheckpointsChanged =
    git diff --cached --name-only --diff-filter=MD -- 'docs/progress/*.md'

if ($historicalCheckpointsChanged)
{
    Write-Warning @"
An existing checkpoint was modified or deleted.
Checkpoints are historical records; change one only to correct a factual error.
"@
}
