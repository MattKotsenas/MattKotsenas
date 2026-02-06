---
title: "Running post setup commands on Azure VMs with SetupComplete2"
description: Trying to enable the Administrator account on a custom VM image for Azure lead down a rabbit hole
date: 2020-05-12T04:19:48.000Z
tags:
 - azure
 - windows
slug: "azure-setupcomplete2"
showHero: true
---

Trying to enable the Administrator account on a custom VM image for Azure lead down a rabbit hole. In the hopes of 
preventing others from wasting time -- I mean, learning very important information -- I'm going to attempt to add 
a bit more documentation around a set of rather obscure features, that, when used together, can be helpful when
creating custom Windows images for use in Azure.

## The background
This all started when I wanted to create a [custom Windows image][custom-image] for a set of VMs in Azure. Creating
custom images can be useful if you want a set of VMs (or VM scale sets) with a set of pre-installed software packages,
drivers, etc. Documentation to customize VMs usually steers developers towards [Custom Script Extensions][custom-script-extensions],
however I have two problems with custom script extensions:

1. They can be slow to deploy
2. The customization is part of VM creation, which puts software installation in the critical path of VM startup

Having a known-good image ready to go avoids both issues.

## My problem
For reasons that aren't really important, my OS of choice for this project was Windows 10, a _client_ operating system,
and not Windows Server. Creating the custom images and starting them in Azure worked fine, but I soon discovered that
I was unable to Remote Desktop into the VMs. [Resetting][reset-vm-password] the administrator password (even to the same value)
seemed to fix the problem.

After quite a bit of digging, I discovered two differences in behavior between server and client editions of Windows when using
[Sysprep][sysprep]:

1. On server, sysprep resets the Administrator password; on client it does not
2. On server, sysprep leaves the Administrator account enabled; on client it disables it

Knowing those two facts explains why the Azure script "EnableAdminAccount" that you can view on any Azure VM under the "Run command"
blade in the portal looks like this:

```powershell
$adminAccount = Get-WmiObject Win32_UserAccount -filter "LocalAccount=True" | ? {$_.SID -Like "S-1-5-21-*-500"}
if($adminAccount.Disabled)
{
  Write-Host Admin account was disabled. Enabling the Admin account.
  $adminAccount.Disabled = $false
  $adminAccount.Put()
} else
{
  Write-Host Admin account is enabled.
}
```
Running that command, even without changing the password, fixed my issue. Now we're getting somewhere!

SID 500 is a [well known security identifier][well-known-sids] for the built-in Administrator account. That means that this script
works even if the Administrator account is renamed. Viewing `$adminAccount.Name` confirms that when setting a username and
password in Azure, Azure renames the Administrator account instead of creating a new account.

## Possible solutions
Now that the problem's understood, there are three main ways to fix the problem that I see:

1. Use a custom script extension to enable the account
2. Use [AdditionalUnattendContent][additionalunattendcontent] to specify a [FirstLogonCommand][firstlogoncommand] that enables the account
3. Use the even more obscure [SetupComplete2.cmd][setupcomplete2-docs] to run the enable script

Options 1 and 2 have the drawback that the _user_ of the image must deploy the image "correctly" in order to support Remote Desktop.
If a user forgets to apply the custom script extension or supply the additional unattend content, the image will be broken. Both solutions
put custom images a tier below Azure's own Marketplace Images, which I didn't like, so unsurprisingly I decided to investigate SetupComplete2.cmd.

### SetupComplete2.cmd
A name like "SetupComplete2" suggests that there's a SetupComplete1, right? Well, you'd be correct; if Windows setup [completes successfully][setupcomplete],
it will automatically run the file `%WINDIR%\Setup\Scripts\SetupComplete.cmd` if present. So, adding a SetupComplete.cmd that enables the
Administrator account should fix things!

Unfortunately for us, Azure can use SetupComplete.cmd as part of its own provisioning process, overwriting our file. At the time of this blog
post, the contents of that file are this (though are probably subject to change):

```cmd
@ECHO OFF && SETLOCAL && SETLOCAL ENABLEDELAYEDEXPANSION && SETLOCAL ENABLEEXTENSIONS

REM Execute this script from %WINDIR%\Setup\Scripts
CALL %SystemRoot%\OEM\SetupComplete.cmd
```

Which is just a "stub" that calls off into another script in the "OEM" folder. Looking at that file:

```cmd
@ECHO OFF && SETLOCAL && SETLOCAL ENABLEDELAYEDEXPANSION && SETLOCAL ENABLEEXTENSIONS

ECHO SetupComplete.cmd BEGIN >> %windir%\Panther\WaSetup.log

TITLE SETUP COMPLETE

REM execute unattend script
cscript %SystemRoot%\OEM\Unattend.wsf //Job:setup //NoLogo //B /ConfigurationPass:oobeSystem >> %windir%\Panther\WaSetup.log

ECHO SetupComplete.cmd END >> %windir%\Panther\WaSetup.log

IF EXIST %SystemRoot%\OEM\SetupComplete2.cmd (
	ECHO Execute SetupComplete2.cmd >> %windir%\Panther\WaSetup.log
	CALL %SystemRoot%\OEM\SetupComplete2.cmd
)
```

Lucky for us, there's hook! Azure's SetupComplete.cmd looks for a `%SystemRoot%\OEM\SetupComplete2.cmd` file and runs it. So, we can put our
script in SetupComplete2.cmd and wait for the Azure provisioning process to call us. Since I like writing PowerShell a whole lot more than
batch, here's what I add to our [Packer][packer] script that creates our custom images:

```powershell
<#
.SYNOPSIS
Write a script to the target machine that OOBE will call to enable the built-in Administrator account (i.e. SID 500, regardless of its name).

.DESCRIPTION
OOBE supports running a custom script after setup completes named C:\Windows\Setup\Scripts\SetupComplete.cmd (see https://docs.microsoft.com/en-us/windows-hardware/manufacture/desktop/add-a-custom-script-to-windows-setup).
However, Azure's provisioning process uses this script (overwriting if necessary) to bootstrap its own
OOBE process. Luckily, Azure's OOBE process leaves a hook for us at the end of its process by running the script
C:\OEM\SetupComplete2.cmd, if present.

This script writes a SetupComplete2.ps1 that enables the built-in Administrator account. It does so by searching for SID 500, so that the script
works even if the Administrator account is renamed (which Azure does). Then a SetupComplete2.cmd is written to call our PowerShell script, since
the hook requires the file to be named "SetupCompelete2.cmd".

.NOTES
To support further downstream customization, this script looks for a C:\OEM\SetupComplete3.cmd, and if found, runs it.
#>

$ErrorActionPreference = "Stop"
Set-StrictMode -version Latest


$path = "$($Env:SystemRoot)\OEM"

New-Item -ItemType Directory -Path $path -Force

# Use single-quote for the here-string so the text isn't interpolated
@'
$adminAccount = Get-WmiObject Win32_UserAccount -filter "LocalAccount=True" | ?{$_.SID -Like "S-1-5-21-*-500"}
if($adminAccount.Disabled)
{
    Write-Host "Admin account was disabled. Enabling the Admin account."
    $adminAccount.Disabled = $false
    $adminAccount.Put()
}
else
{
    Write-Host "Admin account is enabled."
}

# Since we are using SetupComplete2.cmd, add a hook for future us to use SetupComplete3.cmd
if (Test-Path $Env:SystemRoot\OEM\SetupComplete3.cmd)
{
    & $Env:SystemRoot\OEM\SetupComplete3.cmd
}
'@ | Out-File -Encoding ASCII -FilePath "$path\SetupComplete2.ps1"

"powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File %~dp0SetupComplete2.ps1" | Out-File -Encoding ASCII -FilePath "$path\SetupComplete2.cmd"
```

Note that in keeping with the theme, my script runs `SetupComplete3.cmd` if present so that consumers of my custom image can
apply their own customization without needing to hook or replace my script.

## Final thoughts

While I expect this issue to be somewhat niche, piecing this all together took a solid week of poking, prodding, and wading through
docs. If I can save at least one other person the trouble, then it's worth it.

I've also reported this issue to the Windows Provisioning team privately for consideration; unfortunately, I don't have a bug
or feature request link I can share.

If I've made any mistakes or you have thoughts on how to improve on this scenario, please feel free to open an issue or
provide a Pull Request to this post!

[custom-image]: https://docs.microsoft.com/en-us/azure/virtual-machines/windows/capture-image-resource
[custom-script-extensions]: https://docs.microsoft.com/en-us/azure/virtual-machines/extensions/custom-script-windows
[reset-vm-password]: https://docs.microsoft.com/en-us/azure/virtual-machines/troubleshooting/reset-rdp
[sysprep]: https://docs.microsoft.com/en-us/windows-hardware/manufacture/desktop/enable-and-disable-the-built-in-administrator-account#configuring-the-built-in-administrator-password
[well-known-sids]: https://support.microsoft.com/en-us/help/243330/well-known-security-identifiers-in-windows-operating-systems
[additionalunattendcontent]: https://docs.microsoft.com/en-us/dotnet/api/microsoft.azure.management.compute.models.additionalunattendcontent?view=azure-dotnet
[firstlogoncommand]: https://docs.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-shell-setup-firstlogoncommands
[setupcomplete2-docs]: https://docs.microsoft.com/en-us/dynamics-nav/setupcomplete2.cmd-file-example
[setupcomplete]: https://docs.microsoft.com/en-us/windows-hardware/manufacture/desktop/add-a-custom-script-to-windows-setup
[packer]: https://www.packer.io/
