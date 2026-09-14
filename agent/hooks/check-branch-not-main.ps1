# Fails when the repository at RepoPath has main or master checked out. The rule is the
# workflow line in AGENTS.md: all work on dev or a feature branch, never main.
#
#   .\check-branch-not-main.ps1 -RepoPath C:\path\to\repo
#
# Prints one line and exits 1 on main; exits 0 otherwise, including when RepoPath is not a
# repository or HEAD is detached. A check that cannot run must not block a tool; the pre-commit
# hook is the floor. symbolic-ref, not rev-parse: it names the branch before its first commit
# too, and a fresh repository on main is exactly where the first commit lands by accident.
[CmdletBinding()]
param([Parameter(Mandatory)] [string] $RepoPath)
$branch = & git -C $RepoPath symbolic-ref --short HEAD 2>$null
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($branch)) { exit 0 }
$branch = ([string]$branch).Trim()
if ($branch -eq 'main' -or $branch -eq 'master') {
    Write-Output ("The checked-out branch is {0}. Work happens on dev or a feature branch; nothing is committed to {0} directly." -f $branch)
    exit 1
}
exit 0
