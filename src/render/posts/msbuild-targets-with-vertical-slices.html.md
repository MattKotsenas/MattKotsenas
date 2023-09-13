---
title: "Use vertical slices with MSBuild Directory.Build.props and Directory.Build.targets files"
xx cover: /img/ignoreif-mstest/cover.png
isPost: true
active: true
xx excerpt: Create a custom attribute to extend MSTest to programmatically skip / ignore tests based on factors like OS or framework version
xx postDate: '2019-03-20 10:38:20 GMT-0700'
tags:
 - .net
 - msbuild
---

## Background

`Directory.Build.props` and `Directory.Build.targets` are two files that allow you to set default MSBuild items,
properties, and targets for all projects in a directory. Think `.editorconfig` but for MSBuild. Common use cases for
these files are things like:

- Setting the default .NET configuration like target framework version, C# language version, and reference type nullability
- Setting default assembly metadata like author, repository URL, version, and license
- Adding common analyzers and source generators to all projects

If you'd like to learn more about Directory.Build.props/targets, you can check out
[Gary Woodfine's blog][directory-build-props-blog] and the official [Microsoft docs][directory-build-props-msdn]. To
learn more about .props and .targets files generally, check out the [Customizing MSBuild docs][props-targets-msdn].

### Overriding .props and .targets files

> NOTE: From this point on I'll use "Directory.Build.props" to refer to both the .props and .targets files for brevity.

Directory.Build.props is designed for overrides via placing another file "closer" to the code. As an example, a
Directory.Build.props placed in the `/tests` directory can override default settings placed in the root directory (see
background docs for more information). That mechanism works great for code organized like this:

```
MyRepo
|-- Directory.Build.props
|-- src
|   |-- Directory.Build.props
|   |-- App1
|   |-- App2
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
|-- App2
|   |-- src
|   |-- test
```

because there's no common root that all projects share besides the root. If your code it organized this way, a common
workaround is to use the base .props file and use [MSBuild conditions][msbuild-conditions] to only apply the appropriate
properties to a given project. 

For example, let's say you want all tests projects to automatically reference the [FluentAssertions][fluentassertions]
NuGet, you can add this to your Directory.Build.targets:

> NOTE: This snippet must go in a _.targets_ file, as it is using the `IsTestProject` property that's defined (or not)
> in each project. Putting it in the .props file may result in incorrect behavior, as the .props file is imported
> before each project can customize its properties. See [Customize your build][props-targets-msdn] for more information.

```xml
<Project>

  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <PackageReference Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

</Project>
```

In this example, we use the .NET well-known property of `IsTestProject` to conditionally include the FluentAssertions
library into all test projects.

## Accumulation of spaghetti code

If left unchecked, Directory.Build.props tends to accumulate a lot of cruft of workarounds for bugs, fixes for edge cases,
customizations for libraries no longer in use, etc. Additionally, because some scenarios require using both the .props
and .targets files, it can be difficult to understand _why_ some sections exist and how the interact with the other
sections.

To illustrate my point, here's a sample setup for a side project that packages a NuGet CLI tool and MSBuild task. The
specifics of these files are less important than the general pattern they outline, so don't worry about any one feature
too much. I'm going to list three files, `Directory.Build.props` and `Directory.Build.targets`, which we've discussed at
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

    <!-- This can be removed once the fix for https://github.com/dotnet/reproducible-builds/issues/19 is released -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="All" />
  </ItemGroup>
  <Sdk Name="DotNet.ReproducibleBuilds.Isolated" />

  <PropertyGroup>
    <!--
      Defining and using artifacts path manually in preparation for .NET 8's artifacts output format.
      See https://github.com/dotnet/docs/issues/36446
    -->
    <ArtifactsPath>$(RepoRoot)/artifacts</ArtifactsPath>
  </PropertyGroup>
</Project>
```

**Directory.Build.targets**

```xml
<Project>
  <!-- Polyfill -->
  <PropertyGroup>
    <LangVersion Condition="'$(UsePolyfill)' == 'true'">latest</LangVersion>
  </PropertyGroup>
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
    <PackageVersion Include="DotNet.ReproducibleBuilds" Version="1.1.1" />
    <!-- TODO: This can be removed once the fix for https://github.com/dotnet/reproducible-builds/issues/19 is released -->
    <PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" PrivateAssets="All" />

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
2. There are several "workarounds" or (hopefully) temporary additions
3. A "feature" is spread across multiple files; for instance our `IsShipping` property appears in both .props and
  .targets files, while our Polyfill feature is in both .targets and Packages files
4. "Features" are interleaved; without a lot of discipline, it's easy to end up in a situation where a feature like
  `IsShipping` is smeared across a file in multiple places and intermingled with other features

## Organizing your files in vertical slices / features

With that context in mind, how should you go about organizing your features into Directory.Build.props (and related)
files?

One way to cleanly separate our concerns organize our code around features or [vertical slices][vertical-slice-architecture].
Rather than rely on comments to delineate sections and signal intent, physically group related functionality into files.

Using the above example, the code would be physically organized like this:

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
|   |   |-- Polyfill
|   |   |   |-- Polyfill.props
|   |   |   |-- Polyfill.targets
|   |   |-- ReproducibleBuilds
|   |   |   |-- ReproducibleBuilds.props
|   |   |   |-- ReproducibleBuilds.targets
|   |   |-- Shipping
|   |   |   |-- Shipping.props
|   |   |   |-- Shipping.targets
|   |   |-- TestProjects
|   |   |   |-- TestProjects.props
|   |   |   |-- TestProjects.targets
|   |   |-- WorkaroundEditorConfigLinks
|   |   |   |-- WorkaroundEditorConfigLinks.props
|   |   |   |-- WorkaroundEditorConfigLinks.targets
|-- App1
|   |-- src
|   |-- test
|-- App2
|   |-- src
|   |-- test
```

We've split the features into their own sets of .props and .targets files, each named for the piece of functionality
they provide (granularity is up to you). Your root files contain no functionality, and instead only import the features
like this:

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

From here it's much easier to understand what features are being included. Each feature, no longer cluttered amonst the
others, is also able to signal more easily, and comments can be used to explain _why_ rather than as a separator.

Showing each feature would make this post long and boring, so I'll only show the `TestProjects` directory as an example:

**TestProjects.props**

```xml
<Project>
  <ItemGroup>
    <!-- Central Package Management for test helpers -->
    <PackageVersion Include="FluentAssertions" Version="6.12.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Central Package Management for xUnit + dependencies -->
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.7.2" />
    <PackageVersion Include="xunit" Version="2.5.0" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.5.0" />
    <PackageVersion Include="coverlet.collector" Version="6.0.0" />
  </ItemGroup>

</Project>
```

**TestProjects.targets**

```xml
<Project>
  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <!-- Add FluentAssertions to test projects and add global using -->
    <PackageReference Include="FluentAssertions" />

    <Using Include="FluentAssertions" />
  </ItemGroup>

  <ItemGroup Condition="'$(IsTestProject)' == 'true'">
    <!-- Add xunit test harness references and add global using -->
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="coverlet.collector">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>

    <Using Include="Xunit" />
    <Using Include="Xunit.Abstractions" />
  </ItemGroup>
</Project>
```

Organizing your properties this way has several benefits:

1. Each feature is specified in one place and free of irrelevant details
2. Your package versions can also be managed in the props file, keeping dependencies close to the functionality. Note
  that this pattern works even when you're using Central Package Management. As an added bonus, Dependabot can still
  follow the `<Import>` chain and update dependencies (I haven't used Renovate, please let me know if that tool also
  works / has problems)
3. _Removing_ a feature, such as our .editorconfig workaround, is as simple as deleting the folder and corresponding
  imports. This pattern prevents a major source of cruft where parts of features / workarounds are incompletely removed
4. Sharing between projects is much easier; this pattern also helps avoid the common cruft accumulation of copy / pasting
  an ever-growing .props file from project to project

## Wrapping up

Directory.Build.props and NuGet Central Package Management are two great tools to simplify maintenance of .NET projects,
_especially_ large projects and monorepos. However, their usefulness also makes them prime candidates for dumping grounds.
Using vertical slices / features as an organization principle brings some sanity back to working with MSBuild.

[directory-build-props-blog]: https://garywoodfine.com/what-is-this-directory-build-props-file-all-about/
[directory-build-props-msdn]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-by-directory?view=vs-2022
[props-targets-msdn]: https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-your-build?view=vs-2022
[msbuild-conditions]: https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-conditions?view=vs-2022
[fluentassertions]: https://fluentassertions.com/
[nuget-central-package-management]: https://devblogs.microsoft.com/nuget/introducing-central-package-management/
[vertical-slice-architecture]: https://www.jimmybogard.com/vertical-slice-architecture/
