---
title: "Profiling and asynchronous initialization to improve PowerShell startup"
cover: /img/pwsh-profiling-async-startup/cover.png
isPost: true
active: true
excerpt: Initialize your PowerShell profile asynchronously using idle events
postDate: '2024-05-24 12:15:11 GMT-0800'
tags:
 - PowerShell
---

## Scenario

As an avid terminal user, my PowerShell [$PROFILE](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_profiles?view=powershell-7.4)
is full of customizations to make working on the terminal more enjoyable. However, each line in the profile comes at the
cost of increased startup time.

Recently my profile's startup time grew to the point that it started to interrupt my flow, so I went looking for
ways to improve performance.

### Profiling

The first tactic should be to improve the code in your profile. The PowerShell team has the great blog post
"[Optimizing your $Profile](https://devblogs.microsoft.com/powershell/optimizing-your-profile/)" which suggests some
techniques to improve performance. It also recommends the excellent [PSProfiler](https://github.com/IISResetMe/PSProfiler)
tool for measuring and improving performance.

In my case, my long startup times were caused by 4 lines:

```
Count  Line       Time Taken Statement
-----  ----       ---------- ---------
    1     1  **00:00.4757793 oh-my-posh init pwsh | Invoke-Expression
    1     2    00:00.0010184 $env:POSH_GIT_ENABLED = $true
    0     3    00:00.0000000
    1     4  **00:00.6424778 Import-Module -Name Terminal-Icons
    0     5    00:00.0000000
    1     6  **00:00.2414747 Import-Module z
    0     7    00:00.0000000
    0     8    00:00.0000000 # Load up everything in the scripts folder
    0     9    00:00.0000000 foreach ($scriptFile in (Get-ChildItem -Path $PSScriptRoot\scripts -Recurse -Include *.ps1))
    0    10    00:00.0000000 {
    9    11    00:00.0736413   . $scriptFile.FullName
    0    12    00:00.0000000 }
    1    13    00:00.0067889 . $PSScriptRoot\aliases.ps1
    0    14    00:00.0000000
    1    15    00:00.0007204 $Env:PYTHONIOENCODING='utf-8'
    1    16  **00:00.3904604 iex "$(thefuck --alias)"
```

1. [Oh My Posh](https://ohmyposh.dev/)
2. [Terminal-Icons](https://github.com/devblackops/Terminal-Icons)
3. [z](https://github.com/badmotorfinger/z)
4. [thefuck](https://github.com/nvbn/thefuck)

In my case, these modules are pretty important to my daily workflow and I was unwilling to give them up. Instead, I went
looking for alternatives.

### Async / background initialization

My approach was to find a way to make the default PowerShell prompt available immediately and then swap in the fully
loaded environment.

After a few false starts (see [Appendix: Runspaces](#runspaces)), I found that Register-EngineEvent would work for my
purposes.

#### Using PowerShell's Idle event

[Register-EngineEvent](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.utility/register-engineevent?view=powershell-7.4)
has the ability to schedule work for when PowerShell is idle. Given a registration like this:

```powershell
Register-EngineEvent -SourceIdentifier PowerShell.OnIdle -MaxTriggerCount 1 -Action { $Host.UI.WriteLine("Hello, World!") }
```

"Hello, World!" will be run after the PowerShell prompt goes idle.

![Example of using Register-EngineEvent to write to the host][img-example-idle-event]

> NOTE: `$Host.UI` is used in this example to force the action to use the parent's output. Using `echo` would instead
> direct the output to the event handler's job object.

#### Using modules to access the global scope

One complication of the event handler is that it uses
[jobs](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_jobs?view=powershell-7.4)
to run the action in the background, and jobs use a new
[scope](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_scopes?view=powershell-7.4).
Thus, by default, any code run in the idle event _won't_ be available to the prompt.

There are multiple ways to access the global scope in PowerShell, but the one I found to be the simplest and easiest
is to use modules.

Modules support loading into the
[global scope](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/import-module?view=powershell-7.4#-global).
Adding the `-Global` parameter to `Import-Module` ensures that the module is available in the prompt.

For code that isn't already in a module, the `New-Module` cmdlet can be used to make a module on-the-fly. For example,
this loads `thefuck` (which creates a function) into the global namespace:

```powershell
New-Module -ScriptBlock { iex "$(thefuck --alias)" } | Import-Module -Global
```

#### Putting it all together

The last step is to wire the pieces together in `$PROFILE`.

PowerShell will use the default prompt until the profile is loaded, so consider writing a simple prompt that signals
that the profile is still loading:

```powershell
function prompt {
  # We will override this prompt, however because it is loading async we want to communicate that the real prompt is still loading.
  "[async init]: PS $($executionContext.SessionState.Path.CurrentLocation)$('>' * ($nestedPromptLevel + 1)) ";
}
```

I chose to break each module into its own callback to further increase parallelism. Here's an example of loading the same
tools asynchronously.

```powershell
@(
  {
    oh-my-posh init pwsh | Invoke-Expression
    $Env:POSH_GIT_ENABLED = $true
  },
  {
    Import-Module -Name Terminal-Icons -Global
  },
  {
    Import-Module -Name z -Global
  },
  {
    $Env:PYTHONIOENCODING='utf-8'
    New-Module -Name thefuck -ScriptBlock { iex "$(thefuck --alias)" } | Import-Module -Global
  }
) | Foreach-Object { Register-EngineEvent -SourceIdentifier PowerShell.OnIdle -MaxTriggerCount 1 -Action $_ } | Out-Null
```

And here's PowerShell being interactive instantly with the pretty prompt rendering once loaded.

![Example running an interactive shell that loads in the backgroun][img-example-async]

Hope this makes your day a bit more productive! 🧑‍💻

### Appendix

#### Avoiding conflicts with PSReadline

[PSReadline](https://github.com/PowerShell/PSReadLine) supports highlighting a part of the prompt to signal syntax errors
before pressing enter. Powerline-esque tools like oh-my-posh generally turn this feature off, as it can conflict with
their own rendering.

Because we're moving initialization out of the startup code path, you may want to disable this feature yourself:

```powershell
# Disable the "make the prompt red during parse error" because it conflicts with oh-my-posh
Set-PSReadLineOption -PromptText ''
```

### Runspaces

Initially, I assumed that somehow
[runspaces](https://learn.microsoft.com/en-us/powershell/scripting/developer/hosting/creating-runspaces?view=powershell-7.4)
would be involved.

Searching lead me to the following two posts:

- https://gist.github.com/instance-id/1b20852728cc34a70e0ba93527a41c34
- https://fsackur.github.io/2023/11/20/Deferred-profile-loading-for-better-performance/

Both examples used a similar approach:

1. Start up a child runspace
2. Attach a state / context object
3. Run $PROFILE in the runspace asynchronously
4. Check for completion and marshall the loaded state into the parent runspace

Both examples _mostly_ worked, but were flaky or fragile. I'd occasionally experience a crash on startup, or some modules
would capture state from the child runspace and wouldn't work correctly in the parent runspace.

Nonetheless, I'm very grateful for both examples as they helped me understand PowerShell internals better and also
clarified the async loading user experience. 

[img-example-idle-event]: /img/pwsh-profiling-async-startup/example-idle-event.gif
[img-example-async]: /img/pwsh-profiling-async-startup/example-async.gif
