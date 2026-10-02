param(
    [string]$Root = (Join-Path $env:TEMP 'flowmate-d1-demo'),
    [string]$ExeDir = (Join-Path $PSScriptRoot '..\bin\Debug\net10.0-windows10.0.22621.0')
)
# Seeds a throwaway data root (and a sample gallery next to the exe) so the UI has something to show.
if (Test-Path $Root) { Remove-Item $Root -Recurse -Force }
$lib = Join-Path $Root 'roaming\Library'
New-Item -ItemType Directory -Force (Join-Path $lib 'shortcuts') | Out-Null

function Txt($s) { @{ t = 'text'; parts = @($s) } }
function Shortcut($name, $icon, $color, $actions, $extra = @{}) {
    $id = [guid]::NewGuid().ToString()
    $o = @{ id = $id; name = $name; icon = $icon; color = $color; sortOrder = 0; actions = $actions; description = $extra.description; pinned = [bool]$extra.pinned; folderId = $extra.folderId; alias = $extra.alias; acceptsInput = $extra.acceptsInput }
    foreach ($k in @($o.Keys)) { if ($null -eq $o[$k]) { $o.Remove($k) } }
    $o | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $lib "shortcuts\$id.json")
    return $id
}

$folderId = [guid]::NewGuid().ToString()
@(@{ id = $folderId; name = 'Work'; icon = 'Briefcase24'; sortOrder = 0 }) | ConvertTo-Json -AsArray | Set-Content (Join-Path $lib 'folders.json')

$hello = Shortcut 'Say Hello' 'HandWave24' 'orange' @(
    @{ uid = 'a1'; actionId = 'content.text'; parameters = @{ text = (Txt 'Hello from Flowmate!') } },
    @{ uid = 'a2'; actionId = 'ui.showResult'; parameters = @{} }) @{ description = 'Shows a friendly greeting'; pinned = $true; alias = 'hi' }
$null = Shortcut 'Ask My Name' 'Person24' 'teal' @(
    @{ uid = 'b1'; actionId = 'ui.ask'; parameters = @{ prompt = (Txt 'What is your name?') } },
    @{ uid = 'b2'; actionId = 'ui.showResult'; parameters = @{} }) @{ pinned = $true; acceptsInput = @('Text'); alias = 'name' }
$null = Shortcut 'Broken Example' 'Warning24' 'red' @(@{ uid = 'c1'; actionId = 'does.notExist'; parameters = @{} }) @{ description = 'Fails on purpose' }
$null = Shortcut 'Morning Routine' 'WeatherSunny24' 'yellow' @(@{ uid = 'd1'; actionId = 'content.text'; parameters = @{ text = (Txt 'Good morning') } }) @{ folderId = $folderId; pinned = $true }
$null = Shortcut 'Weekly Report' 'DocumentText24' 'blue' @(@{ uid = 'e1'; actionId = 'content.text'; parameters = @{ text = (Txt 'Report') } }) @{ folderId = $folderId }
$null = Shortcut 'Clean Downloads' 'Delete24' 'purple' @(@{ uid = 'f1'; actionId = 'content.text'; parameters = @{ text = (Txt 'x') } })
$null = Shortcut 'Focus Mode' 'HeadphonesSoundWave24' 'indigo' @(@{ uid = 'g1'; actionId = 'content.text'; parameters = @{ text = (Txt 'x') } }) @{ pinned = $true }
$null = Shortcut 'Quick Note' 'Notepad24' 'green' @(@{ uid = 'h1'; actionId = 'content.text'; parameters = @{ text = (Txt 'x') } })

@(
    @{ id = [guid]::NewGuid().ToString(); enabled = $true; trigger = @{ triggerId = 'app.opened'; parameters = @{ app = @{ t = 'app'; id = 'Spotify'; name = 'Spotify' } } }; conditions = @(@{ conditionId = 'cond.timeBetween'; parameters = @{ from = @{ t = 'time'; v = '09:00' }; to = @{ t = 'time'; v = '17:00' } } }); shortcutId = $hello; runMode = 'immediately'; notifyWhenRun = $false; cooldownSeconds = 0; createdAt = (Get-Date).ToString('o') },
    @{ id = [guid]::NewGuid().ToString(); enabled = $false; trigger = @{ triggerId = 'time.daily'; parameters = @{ time = @{ t = 'time'; v = '07:30' }; days = @{ t = 'list'; items = @(@{ t = 'text'; parts = @('mon') }, @{ t = 'text'; parts = @('fri') }) } } }; conditions = @(); shortcutId = $hello; runMode = 'askFirst'; notifyWhenRun = $true; cooldownSeconds = 60; createdAt = (Get-Date).AddMinutes(1).ToString('o') }
) | ConvertTo-Json -Depth 12 -AsArray | Set-Content (Join-Path $lib 'automations.json')

@(
    @{ id = [guid]::NewGuid().ToString(); abbreviation = ';sig'; text = "Best regards,`nRamin"; enabled = $true },
    @{ id = [guid]::NewGuid().ToString(); abbreviation = ';today'; text = 'Today is {date} and it is {time}. {cursor}'; enabled = $true; folder = 'Writing' },
    @{ id = [guid]::NewGuid().ToString(); abbreviation = ';hi'; text = 'Hi {input:Name}, thanks for {clipboard}'; enabled = $false }
) | ConvertTo-Json -Depth 5 -AsArray | Set-Content (Join-Path $lib 'snippets.json')

if (Test-Path $ExeDir) {
    $g = Join-Path $ExeDir 'gallery'
    New-Item -ItemType Directory -Force $g | Out-Null
    $pkg = @{ format = 'flowmate.shortcut'; version = 1; shortcut = @{ id = [guid]::NewGuid().ToString(); name = 'Resize Images'; description = 'Makes pictures smaller so they are easy to share.'; icon = 'Image24'; color = 'purple'; actions = @(
        @{ uid = 'r1'; actionId = 'content.text'; parameters = @{ text = (Txt 'Resizing') } }, @{ uid = 'r2'; actionId = 'ui.alert'; parameters = @{ message = (Txt 'Done') } }, @{ uid = 'r3'; actionId = 'nope.deleteAll'; parameters = @{} });
        importQuestions = @(@{ actionUid = 'r1'; parameterKey = 'text'; question = 'What should the message say?'; defaultValue = (Txt 'Resizing') }) } }
    $pkg | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $g 'resize-images.flowmate')
    $pkg.shortcut.id = [guid]::NewGuid().ToString(); $pkg.shortcut.name = 'Good Morning'; $pkg.shortcut.description = 'Weather, a spoken greeting and your work apps.'; $pkg.shortcut.icon = 'WeatherSunny24'; $pkg.shortcut.color = 'orange'; $pkg.shortcut.importQuestions = @()
    $pkg | ConvertTo-Json -Depth 12 | Set-Content (Join-Path $g 'good-morning.flowmate')
    '{"sections":[{"title":"Get Started","files":["good-morning.flowmate"]},{"title":"Files & Photos","files":["resize-images"]}]}' | Set-Content (Join-Path $g 'index.json')
}
Write-Output $Root

