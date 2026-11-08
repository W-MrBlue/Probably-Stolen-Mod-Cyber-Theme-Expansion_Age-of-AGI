$ErrorActionPreference = 'Stop'
$targets = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../AgeofAGI/Build/Localization.targets'))
$tempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$fixtureRoot = [System.IO.Path]::GetFullPath((Join-Path $tempParent ('aoa-localization-' + [Guid]::NewGuid().ToString('N'))))
$encoding = New-Object System.Text.UTF8Encoding($false)
try {
    [System.IO.Directory]::CreateDirectory((Join-Path $fixtureRoot 'Localization/Npcs')) | Out-Null
    [System.IO.Directory]::CreateDirectory((Join-Path $fixtureRoot 'Localization/Books')) | Out-Null
    $project = Join-Path $fixtureRoot 'checks.proj'
    [System.IO.File]::WriteAllText($project, @"
<Project>
  <PropertyGroup><TargetDir>$fixtureRoot/out/</TargetDir></PropertyGroup>
  <Import Project="$targets" />
</Project>
"@, $encoding)
    $main = @('# independent fixture', 'item_name,Fixture item', 'empty,', 'escaped,First\nSecond', 'case,lower', 'Case,upper')
    $npc = @('npc_name,Fixture customer', 'npc_line,"First, with comma;Second ""quoted"" line"', ('unicode,' + [char]0x5BA2 + [char]0x4EBA))
    $books = @('book_title,Fixture guide', 'book_body,"First\nSecond, with comma"', 'book_caption,"""Fixture"" GPU"')
    [System.IO.File]::WriteAllLines((Join-Path $fixtureRoot 'Localization/en.csv'), $main, $encoding)
    [System.IO.File]::WriteAllLines((Join-Path $fixtureRoot 'Localization/Npcs/en.csv'), $npc, $encoding)
    [System.IO.File]::WriteAllLines((Join-Path $fixtureRoot 'Localization/Books/en.csv'), $books, $encoding)
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'Localization/fr.csv'), 'fallback,No fragments', $encoding)
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'Localization/de.csv'), 'item_name,Another fixture', $encoding)
    [System.IO.File]::WriteAllText((Join-Path $fixtureRoot 'Localization/Books/de.csv'), 'book_title,Book without NPCs', $encoding)
    $log = & dotnet msbuild $project -t:MergeModLocalizations -nologo -v:quiet 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($log -join [Environment]::NewLine) }
    $output = Join-Path $fixtureRoot 'out/Localization/en.csv'
    $merged = [System.IO.File]::ReadAllLines($output)
    foreach ($line in @($main) + @($npc) + @($books)) {
        if (@($merged | Where-Object { $_ -ceq $line }).Count -ne 1) { throw ('Merge changed or duplicated: ' + $line) }
    }
    if (![System.IO.File]::Exists((Join-Path $fixtureRoot 'out/Localization/fr.csv'))) { throw 'A locale without fragments was not emitted.' }
    if ([System.IO.File]::ReadAllText((Join-Path $fixtureRoot 'out/Localization/de.csv')) -notmatch 'book_title,Book without NPCs') { throw 'A book fragment without an NPC fragment was not merged.' }
    $before = [System.IO.File]::ReadAllText($output)
    foreach ($duplicate in @(
        @{ Path = 'Localization/Npcs/en.csv'; Line = 'item_name,Duplicate' },
        @{ Path = 'Localization/Books/en.csv'; Line = 'item_name,Duplicate main key' },
        @{ Path = 'Localization/Books/en.csv'; Line = 'npc_name,Duplicate NPC key' }
    )) {
        [System.IO.File]::WriteAllLines((Join-Path $fixtureRoot 'Localization/Npcs/en.csv'), $npc, $encoding)
        [System.IO.File]::WriteAllLines((Join-Path $fixtureRoot 'Localization/Books/en.csv'), $books, $encoding)
        [System.IO.File]::AppendAllText((Join-Path $fixtureRoot $duplicate.Path), ($duplicate.Line + "`n"), $encoding)
        $log = & dotnet msbuild $project -t:MergeModLocalizations -nologo -v:quiet 2>&1
        if ($LASTEXITCODE -eq 0 -or ($log -join '') -notmatch 'Duplicate localization key') { throw 'Duplicate keys must fail with a source diagnostic.' }
        if ([System.IO.File]::ReadAllText($output) -cne $before) { throw 'An invalid merge overwrote its previous output.' }
    }
    Write-Output 'PASS: main/NPC/book localization merge preserves Unicode, quoted CSV, semicolons, literal newlines, empty values and case; missing optional fragments work; duplicate keys across all source groups fail before output replacement.'
}
finally {
    # Verify this exact, generated fixture remains directly inside the temporary directory before cleanup.
    if ([System.IO.Path]::GetDirectoryName($fixtureRoot).TrimEnd('\') -ine $tempParent.TrimEnd('\')) { throw 'Unexpected fixture cleanup path.' }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
