---
title: "MSBuild Traversal project best practices"
cover: /img/traversal-project-best-practices/cover.jpeg
socialImage: /img/traversal-project-best-practices/cover-social.jpg
isPost: true
active: true
excerpt: Best practices for using MSBuild traversal projects
postDate: "2025-03-07 09:48:22 GMT-0800"
tags:
  - .net
  - msbuild
---

> [!NOTE]
> This post assumes you're already familiar with the
> [Microsoft.Build.Traversal](https://github.com/microsoft/MSBuildSdks/blob/main/src/Traversal/README.md)
> SDK, a.k.a `dirs.proj` files.

Whether you're new to traversal projects or an experienced user, you may
encounter some common pitfalls. This post covers frequently asked questions and
best practices to help you avoid them.

## Problem: Multiple SDK versions

The easiest way to use a traversal project is like this, where the version of
the project SDK is listed as part of the `Sdk` property:

```xml
<Project Sdk="Microsoft.Build.Traversal/4.1.82">
```

If you have a large project, over time it's likely that someone will
introduce a new version of the Traversal SDK without updating the others.
MSBuild however only allows a single SDK version during a build. Trying to mix
versions results in this warning:

```
MSB4240: Multiple versions of the same SDK 'Microsoft.Build.Traversal' cannot be
specified. The previously resolved SDK version 'value' from location 'value'
will be used and the version 'value' will be ignored.
```

To complicate matters, this warning may appear or disappear depending on which
projects are included in the build.

To sidestep this entire issue and force a consistent SDK version across your
codebase, instead specify the SDK version in your `global.json` file like this:

```json
{
  "msbuild-sdks": {
    "Microsoft.Build.Traversal": "4.1.82"
  }
}
```

## Problem: Visual Studio / .sln files

Traversal projects offer a few advantages over `.sln` files (including the new
`.slnx` format).

First, traversal projects can reference other traversal projects. In large
codebases, this is incredibly useful because it means each component can
structure their code as they like while also making it easy to build multiple
components by maintaining a single meta-project.

Second, with traversal files there's no duplication between projects and
solutions. With .sln and .slnx files, adding or removing a project reference
requires _also_ updating the sln file. Traversal projects, by their nature, use
project references as the source of truth and thus cannot get out-of-sync.

If you adopt traversal projects, you should stop tracking sln files in your
version control system by adding an entry to your `.gitignore` like this:

```diff
+# Ignore sln files in favor of Traversal projects
+*.sln
+*.slnx
```

and delete them from source control with a script like this:

```bash
git ls-files "*.sln" "**/*.sln" | xargs git rm
```

If your workflow relies on .sln files, such as for Visual Studio users,
consider using a tool like [SlnGen](https://microsoft.github.io/slngen/) to
generate .sln files on-the-fly.

## Problem: MSB1008: Only one project can be specified

If you follow the approach above (using traversal projects to organize your
build while generating .sln files on the fly for editing) you may end up with
many untracked and stale .sln files littered around your codebase. This can lead
to errors when you run commands like `dotnet build` such as this:

```
MSB1008: Only one project can be specified
```

because the dotnet command finds both a `.proj` and `.sln` file in the same
directory.

To avoid the need to constantly disambiguate between projects, you can instruct
MSBuild to ignore certain extensions using the `-ignoreProjectExtensions`
command-line switch
([docs](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference?view=vs-2022)).
Better yet, create and check in a `Directory.Build.rsp` file that includes this
switch:

```
# Since we use traversal projects (dirs.proj), ignore any stale .sln files.
# These are generated, .gitignored, and may cause confusion.
-ignoreProjectExtensions:.sln,.slnx
```

to ensure that all command line builds inherit this option by default
([docs](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-response-files?view=vs-2022)).

Note that previously, `*.rsp` files were excluded by the dotnet .gitignore
template, so be sure that you aren't ignoring this file. This has been
[fixed](https://github.com/dotnet/sdk/pull/42401) for .NET 10.

## Problem: Every Visual Studio window is now named "dirs"

Visual Studio uses the file name as the window name. When using traversal
projects, this means every VS instance ends up named "dirs", which isn't helpful.

To set a meaningful solution name, define `SlnGenProjectName` in your dirs.proj
file:

```xml
<PropertyGroup>
  <SlnGenProjectName>MyCoolProject</SlnGenProjectName>
</PropertyGroup>
```

You can also customize other options, such as mirroring the folder structure in
the solution file. See
[Configuring SlnGen](https://microsoft.github.io/slngen/#configuring-slngen)
for the full list of options.

If you have multiple dirs.proj files, managing solution names manually can be
tedious. Instead, set default properties globally in a Directory.Build.props
file:

```xml
<PropertyGroup>
  <SlnGenFolders>true</SlnGenFolders>
  <SlnGenProjectName>$([System.IO.Path]::GetFileName($([System.IO.Path]::GetDirectoryName($(MSBuildProjectFullPath)))))
  </SlnGenProjectName>
</PropertyGroup>
```

This ensures:

- Folder structure is reflected in .sln files by default
- The solution name defaults to the parent folder name, avoiding the "dirs"
  issue automatically.

This approach keeps things consistent without needing to set SlnGenProjectName
manually in every project file.

## Wrapping up

MSBuild traversal projects can improve maintainability, reduce duplication, and
streamline large codebases. However, they come with unique challenges,
especially in larger teams accustomed to `.sln` files. While moving away from
traditional solution files requires some adjustment, the long-term benefits in
automation and flexibility make it worthwhile. Have additional tips or insights?
Reach out to me and I'll update this post to include the best practices from the
community!
