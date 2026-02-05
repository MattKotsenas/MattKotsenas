---
title: "Simplify MSBuild Directory.Build.props and .targets files with vertical slices"
cover: /img/msbuild-targets-with-vertical-slices/cover.jpg
socialImage: /img/msbuild-targets-with-vertical-slices/cover-social.jpg
excerpt: Use vertical slices / features as an organization mechanism and make Directory.Build.props and .targets files easier to work with
date: 2023-09-14T00:07:20.000Z
tags:
 - dotnet
 - msbuild
slug: "msbuild-targets-with-vertical-slices"
---

## Background

`Directory.Build.props` and `Directory.Build.targets` are two files that allow you to set default MSBuild items,
properties, and targets for all projects under a given directory. Think `.editorconfig` but for MSBuild. Common use
cases for these files are things like:

- Setting default .NET properties such as target framework version, C# language version, and reference type nullability
- Setting sharing package metadata like author, repository URL, version, and license across several packages
- Adding common analyzers and source generators to all projects

Check out [Gary Woodfine's blog][directory-build-props-blog] and the official [Microsoft docs][directory-build-props-msdn]
if you'd like to learn more about Directory.Build.props/targets. Read the [Customizing MSBuild docs][props-targets-msdn]
to learn more about .props and .targets files more generally.

### Overriding .props and .targets files

> NOTE: For brevity, from this point on I'll use "Directory.Build.props" to refer to both the .props and .targets files

Directory.Build.props supports overrides by placing another file "closer" to the code. As an example, a
Directory.Build.props placed in the `/tests` directory can override default settings placed in the root directory. That
mechanism works great for code organized like this:

```
MyRepo
|-- Directory.Build.props
|-- src
|   |-- Directory.Build.props
|   |-- App1
|   |-- App2
|
|-- test
|   |-- Directory.Build.props
|   |-- App1.Tests
|   |-- App2.Tests
```

However, it doesn't work great for code organized like this:

```
MyRepo
|-- Directory.Build.props
|-- App1
|   |-- src
|   |-- test
|
|-- App2
|   |-- src
|   |-- test
```

because there's no common root that all projects share (besides the root itself). If your code is organized this way, a
common workaround is to use the root .props file and [MSBuild conditions][msbuild-conditions] to apply the appropriate
properties to a given project. 

For example, let's say you want all test projects to automatically reference [FluentAssertions][fluentassertions]. You
can add this to your `Directory.Build.props`:

> NOTE: Choosing between .props and .targets can be a tricky decision. See
> [Choose between adding properties to a .props or .targets file][choosing-props-targets] for more information.

```xml
<Project>
  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
</Project>
```

In this example, we use the .NET well-known property of `IsTestProject` to conditionally include our package reference
into all test projects.

## Accumulation of cruft

If left unchecked, Directory.Build.props tends to accumulate a lot of cruft: workarounds for bugs, fixes for edge cases,
customizations for dependencies no longer in use, etc. Additionally, because code is often split across the .props
and .targets files, it can be difficult to understand _why_ some sections exist and how they interact with the other
sections.

To illustrate my point, here's a sample setup for a side project that packages a NuGet CLI tool and MSBuild task. The
specifics of these files are less important than the general pattern they outline, so don't worry about any one section
too much. I'm going to show three files: `Directory.Build.props` and `Directory.Build.targets`, which we've discussed at
length, and add in `Directory.Package.props`, which is used by NuGet's
[Central Package Management][nuget-central-package-management] feature.

**Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
  </PropertyGroup>

  <PropertyGroup>
    <!-- Default IsShipping to true -->
    <IsShipping Condition="'$(IsShipping)' == ''">true</IsShipping>
  </PropertyGroup>

  <!-- https://github.com/dotnet/reproducible-builds -->
  <ItemGroup>
    <PackageReference Include="DotNet.ReproducibleBuilds" PrivateAssets="All" />
  </ItemGroup>
  <Sdk Name="DotNet.ReproducibleBuilds.Isolated" />

  <PropertyGroup>
    <!--
      Defining and using artifacts path manually in preparation for .NET 8's artifacts output format.
      See https://github.com/dotnet/docs/issues/36446
    -->
    <ArtifactsPath>$(RepoRoot)/artifacts</ArtifactsPath>
  </PropertyGroup>

  <!-- Polyfill -->
  <ItemGroup Condition="'$(UsePolyfill)' == 'true'">
    <PackageReference Include="Polyfill">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    <PackageReference>
    <PackageReference Include="System.Memory" Condition="$(TargetFrameworkIdentifier) == '.NETStandard' or $(TargetFrameworkIdentifier) == '.NETFramework' or $(TargetFramework.StartsWith('netcoreapp2'))">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="System.Threading.Tasks.Extensions" Condition="$(TargetFramework) == 'netstandard2.0' or $(TargetFramework) == 'netcoreapp2.0' or $(TargetFrameworkIdentifier) == '.NETFramework'">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>

  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <!-- Add xunit to test projects -->
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>

    <!--
      Add global usings for test projects.
      (see https://devblogs.microsoft.com/dotnet/welcome-to-csharp-10/#combining-using-features)
    -->
    <Using Include="Xunit" />
    <Using Include="Xunit.Abstractions" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <!-- Add FluentAssertions to test projects and add global using -->
    <PackageReference Include="FluentAssertions" />

    <Using Include="FluentAssertions" />
  </ItemGroup>
</Project>
```

**Directory.Build.targets**

```xml
<Project>
  <!-- Polyfill -->
  <PropertyGroup>
    <LangVersion Condition="'$(UsePolyfill)' == 'true'">latest</LangVersion>
  </PropertyGroup>

  <PropertyGroup>
    <!-- Test projects can't also be shipping projects -->
    <IsShipping Condition="'$(IsTestProject)' == true">false</IsShipping>
  </PropertyGroup>

  <Target Name="MapToAbsoluteFilePaths" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' != 'true'">
    <!--
      Work around .editorconfig evaluation bugs in command line builds. See https://github.com/dotnet/roslyn/issues/43371
    -->
    <ItemGroup>
      <_AbsoluteCompile Include="@(Compile->'%(FullPath)')" />
      <Compile Remove="@(Compile)" />
      <Compile Include="@(_AbsoluteCompile)" />
    </ItemGroup>
  </Target>

  <PropertyGroup>
    <!-- Force warnings as errors for shipping projects -->
    <TreatWarningsAsErrors Condition="'$(IsShipping)' =='true'">true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

**Directory.Package.props**

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>

  <ItemGroup>
    <!-- CLI tools -->
    <PackageVersion Include="CliWrap" Version="3.6.4" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="7.0.0" />
    <PackageVersion Include="Spectre.Console" Version="0.47.0" />
    
    <!-- MSBuild task -->
    <PackageVersion Include="Microsoft.Build.Framework" Version="17.7.2" />
    <PackageVersion Include="Microsoft.Build.Utilities.Core" Version="17.7.2" />

    <!-- Polyfill + dependencies -->
    <PackageVersion Include="Polyfill" Version="1.27.1" />
    <PackageVersion Include="System.Memory" Version="4.5.5" />
    <PackageVersion Include="System.Threading.Tasks.Extensions" Version="4.5.4" />

    <!-- Reproducible builds + dependencies -->
    <PackageVersion Include="DotNet.ReproducibleBuilds" Version="1.2.4" />

    <!-- xUnit + dependencies -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.7.2" />
    <PackageVersion Include="xunit" Version="2.5.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.5.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.0" />

    <!-- Test helpers -->
    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>
</Project>
```

Skimming through this code, I hope a few patterns emerge:

1. It's not clear where one "feature" ends and another begins; comments are required to delineate sections
2. There are several workarounds and (hopefully) temporary additions
3. A "feature" is spread across multiple files; for instance our `IsShipping` property appears in both .props and
  .targets files, while our Polyfill and Reproducible builds features are in all three files
4. "Features" are interleaved; without a lot of discipline, it's easy to end up in a situation where a feature like
  `IsShipping` is smeared across a file in multiple places and intermingled with other features

## Organizing your files in vertical slices / features

With that context in mind, how should you go about organizing your features into Directory.Build.props (and related)
files?

One way to cleanly separate our concerns is to organize our code around features or
[vertical slices][vertical-slice-architecture]. Rather than rely on comments to delineate sections and signal intent,
physically group related functionality into directories. A hierarchy such as this reduces clutter and makes it easier
to understand how each feature works.

Using the above example, we can create an `eng/targets` directory and compartmentalize each feature into its own
subdirectory along with a corresponding `.props` and `.targets` file. The code would now be organized like this:

```
MyRepo
|-- Directory.Build.props
|-- Directory.Build.targets
|-- Directory.Package.props
|-- eng
|   |-- targets
|   |   |-- Artifacts
|   |   |   |-- Artifacts.props
|   |   |   |-- Artifacts.targets
|   |   |
|   |   |-- Polyfill
|   |   |   |-- Polyfill.props
|   |   |   |-- Polyfill.targets
|   |   |
|   |   |-- ReproducibleBuilds
|   |   |   |-- ReproducibleBuilds.props
|   |   |   |-- ReproducibleBuilds.targets
|   |   |
|   |   |-- Shipping
|   |   |   |-- Shipping.props
|   |   |   |-- Shipping.targets
|   |   |
|   |   |-- TestProjects
|   |   |   |-- TestProjects.props
|   |   |   |-- TestProjects.targets
|   |   |
|   |   |-- WorkaroundEditorConfigLinks
|   |   |   |-- WorkaroundEditorConfigLinks.props
|   |   |   |-- WorkaroundEditorConfigLinks.targets
|
|-- App1
|   |-- src
|   |-- test
|
|-- App2
|   |-- src
|   |-- test
```

Feel free to use a directory other than `/eng/targets` if you like. `/build/targets` and `/build/props` are other
common choices. It's also up to you to define the granularity of a feature.

Your root files now contain no functionality. Instead they only import the features like this:

**Directory.Build.props**

```xml
<Project>
  <PropertyGroup>
    <RepoRoot>$(MSBuildThisFileDirectory)</RepoRoot>
  </PropertyGroup>

  <Import Project="eng/targets/Artifacts/Artifacts.props" />
  <Import Project="eng/targets/Polyfill/Polyfill.props" />
  <Import Project="eng/targets/ReproducibleBuilds/ReproducibleBuilds.props" />
  <Import Project="eng/targets/Shipping/Shipping.props" />
  <Import Project="eng/targets/TestProjects/TestProjects.props" />
  <Import Project="eng/targets/WorkaroundEditorConfigLinks/WorkaroundEditorConfigLinks.props" />
</Project>
```

**Directory.Build.targets**

```xml
<Project>
  <Import Project="eng/targets/Artifacts/Artifacts.targets" />
  <Import Project="eng/targets/Polyfill/Polyfill.targets" />
  <Import Project="eng/targets/ReproducibleBuilds/ReproducibleBuilds.targets" />
  <Import Project="eng/targets/Shipping/Shipping.targets" />
  <Import Project="eng/targets/TestProjects/TestProjects.targets" />
  <Import Project="eng/targets/WorkaroundEditorConfigLinks/WorkaroundEditorConfigLinks.targets" />
</Project>
```

From here it's much easier to understand which features are being included. Each feature, no longer cluttered amongst the
others, is able to easily signal its intent. Comments can be used to explain _why_ rather than as a separator.

Refactoring each feature would make this post long and boring, so I'll focus on the `Polyfill` directory as an example:

**Polyfill.props**

```xml
<Project>
  <ItemGroup Condition="'$(UsePolyfill)' == 'true'">
    <PackageReference Include="Polyfill">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    <PackageReference>
    <PackageReference Include="System.Memory" Condition="$(TargetFrameworkIdentifier) == '.NETStandard' or $(TargetFrameworkIdentifier) == '.NETFramework' or $(TargetFramework.StartsWith('netcoreapp2'))">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="System.Threading.Tasks.Extensions" Condition="$(TargetFramework) == 'netstandard2.0' or $(TargetFramework) == 'netcoreapp2.0' or $(TargetFrameworkIdentifier) == '.NETFramework'">
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

**Polyfill.targets**

```xml
<Project>
  <PropertyGroup>
    <LangVersion Condition="'$(UsePolyfill)' == 'true'">latest</LangVersion>
  </PropertyGroup>
</Project>
```

Organizing your properties this way has several benefits:

1. Each feature is specified in one place and free of unrelated details
2. Your package versions can also be managed in the props file, keeping dependencies near the functionality. Note that
  this pattern works even when you're using Central Package Management. As an added bonus, Dependabot understands the
  `<Import>` chain and updates dependencies like you'd expect (I haven't used Renovate, please let me know if that tool
  also works / has problems)
3. _Removing_ a feature, such as our .editorconfig workaround, is as simple as deleting the folder and corresponding
  imports. By grouping all parts of the workaround together, we've reduced the chance that some pieces are incompletely
  removed and as a result prevented a major source of cruft
4. Sharing between projects is much easier; we've reduced the temptation to copy /paste an ever-growing .props file from
project to project and avoided another common source of cruft accumulation

## Wrapping up

Directory.Build.props and NuGet Central Package Management are two great tools to simplify maintenance of .NET projects,
_especially_ large projects and monorepos. However, their usefulness also makes them prime candidates for dumping grounds.
Using vertical slices / features as an organization principle brings some sanity back to working with MSBuild.

[directory-build-props-blog]: https://garywoodfine.com/what-is-this-directory-build-props-file-all-about/
[directory-build-props-msdn]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=vs-2022
[props-targets-msdn]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
[choosing-props-targets]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022#choose-between-adding-properties-to-a-props-or-targets-file
[msbuild-conditions]: https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-conditions?view=vs-2022
[fluentassertions]: https://fluentassertions.com/
[nuget-central-package-management]: https://devblogs.microsoft.com/nuget/introducing-central-package-management/
[vertical-slice-architecture]: https://www.jimmybogard.com/vertical-slice-architecture/
