param(
    [Parameter(Mandatory = $true)][string]$InputFile,
    [Parameter(Mandatory = $true)][string]$OutputFile
)

$ErrorActionPreference = 'Stop'
# Read only the documented editing fields, never the original game text or notes.
# This intentionally accepts literal |- blocks or the documented empty string,
# rather than pretending to be a general YAML parser. Unsupported edits fail the
# build with their line number instead of silently changing the player's words.
$lines = [System.IO.File]::ReadAllLines($InputFile)
$entries = [ordered]@{}
$pendingKey = $null
$pendingIndent = -1
$keyCount = 0
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    if ($line.Contains("`t")) { throw "Use spaces, not tabs, in the charm editing file (line $($i + 1))." }
    if ($line -match '^([ ]*)description_key: (CHARM_DESC_[0-9]+(?:_[A-Z]+)?)\s*$') {
        if ($null -ne $pendingKey) { throw "Missing new_description for $pendingKey." }
        $pendingKey = $Matches[2]
        $pendingIndent = $Matches[1].Length
        $keyCount++
        continue
    }
    if ($line -notmatch '^([ ]*)new_description:\s*(.*)$') { continue }
    $indent = $Matches[1].Length
    $style = $Matches[2]
    if ($null -eq $pendingKey -or $indent -ne $pendingIndent) {
        throw "new_description must match its description_key indentation (line $($i + 1))."
    }
    if ($entries.Contains($pendingKey)) { throw "Duplicate charm description: $pendingKey." }
    if ($style -eq '""') {
        $text = ''
    } elseif ($style -eq '|-') {
        $body = [System.Collections.Generic.List[string]]::new()
        while ($i + 1 -lt $lines.Length) {
            $next = $lines[$i + 1]
            if ($next.Trim().Length -eq 0) {
                # YAML blank lines contain no scalar text, even when indented.
                $body.Add('')
                $i++
                continue
            }
            $leading = $next.Length - $next.TrimStart(' ').Length
            if ($leading -le $indent) { break }
            if ($leading -lt $indent + 2) { throw "Indent charm text by two spaces below new_description (line $($i + 2))." }
            # Preserve text verbatim, including spelling, punctuation and trailing
            # spaces. Only remove the YAML block's structural indentation.
            $body.Add($next.Substring($indent + 2))
            $i++
        }
        while ($body.Count -gt 0 -and $body[$body.Count - 1] -eq '') { $body.RemoveAt($body.Count - 1) }
        $text = [string]::Join("`n", $body.ToArray())
    } else {
        throw "Use new_description: |- or new_description: `"`" (line $($i + 1))."
    }
    $entries.Add($pendingKey, $text)
    $pendingKey = $null
}
if ($null -ne $pendingKey) { throw "Missing new_description for $pendingKey." }
if ($keyCount -eq 0) { throw 'No charm description entries were found.' }

$source = [System.Text.StringBuilder]::new()
[void]$source.AppendLine('// Generated from Charm-Descriptions.yaml; edit that file, not this build output.')
[void]$source.AppendLine('using System;')
[void]$source.AppendLine('using System.Collections.Generic;')
[void]$source.AppendLine('namespace HollowMario {')
[void]$source.AppendLine('internal static class CharmDescriptionText {')
[void]$source.AppendLine('internal static readonly Dictionary<string,string> Entries = new Dictionary<string,string>(StringComparer.Ordinal) {')
$active = 0
foreach ($entry in $entries.GetEnumerator()) {
    if ($entry.Value.Length -eq 0) { continue }
    # JSON string escaping is also a C# regular string literal for these fields.
    $literal = ConvertTo-Json -InputObject ([string]$entry.Value) -Compress
    [void]$source.AppendLine(('{{ "{0}", {1} }},' -f $entry.Key, $literal))
    $active++
}
[void]$source.AppendLine('}; } }')
$target = [System.IO.Path]::GetFullPath($OutputFile)
[void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($target))
$output = $source.ToString()
if (-not [System.IO.File]::Exists($target) -or [System.IO.File]::ReadAllText($target) -cne $output) {
    [System.IO.File]::WriteAllText($target, $output, [System.Text.UTF8Encoding]::new($false))
}
Write-Output "Compiled $active charm descriptions verbatim from the editing file."
