# Fails when the text contains an em dash (U+2014). The rule is the Personal conventions line
# of the pillars: no em dashes anywhere, in code, comments, docs, or chat. Every adapter beside
# this file calls it, so what counts as a violation is defined here and nowhere else.
#
#   .\check-em-dash.ps1 -Text "..." [-Label "path or field"] [-FirstLine 1]
#
# Prints one line per offending line and exits 1; prints nothing and exits 0 when clean.
# FirstLine is the line number of the first line of Text in its file, so a caller that hands
# over one hunk at a time still reports the real line. Complexity: O(n) in the length of Text.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
    [string] $Label = 'text',
    [int] $FirstLine = 1
)
$emDash = [string][char]0x2014
if (-not $Text.Contains($emDash)) { exit 0 }
$lines = $Text -split "`r?`n"
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].Contains($emDash)) {
        Write-Output ("{0}:{1} contains an em dash (U+2014). Use a period, a comma, or a colon; the pillars forbid em dashes anywhere." -f $Label, ($FirstLine + $i))
    }
}
exit 1
