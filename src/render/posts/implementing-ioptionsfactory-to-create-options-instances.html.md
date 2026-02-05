---
title: "Implementing IOptionsFactory<T> to create custom options instances"
cover: /img/implementing-ioptionsfactory-to-create-options-instances.png
socialImage: /img/implementing-ioptionsfactory-to-create-options-instances-social.jpg
isPost: true
active: true
excerpt: Recently, I was writing a .NET console app to integrate with a third-party library and had an opportunity to use the IOptions pattern. However, the settings object didn't have a public, parameterless constructor. Here's how I used IOptionsFactory to support these types of options classes...
postDate: '2023-06-15 10:10:21 GMT-0700'
tags:
 - .net
---

## Backstory

Recently, I was writing a .NET console app to integrate with a third-party library. The library
had a configuration object, let's call it `LibSettings`, that controlled various options of the library.

LibSettings is a perfect candidate for the [IOptions pattern] that's prevalent in both .NET and
ASP.NET Core. However, there was one big problem. The [Options docs] say that options classes:

> Must be non-abstract with a public parameterless constructor.

and the library author didn't follow this rule (to be fair, the library predates the IOptions pattern).
Instead, the library uses the factory method pattern to create the settings object like this:

```csharp
LibSettings.Create()
```

I was stuck; without a parameterless constructor, I couldn't use the options pattern, but since `LibSettings`
is owned by a third-party library, I'm not able to change it.

However, it turns out the options pattern is more flexible that that.

## Writing a custom IOptionsFactory

It _used_ to be the case that all options classes needed to satisfy the `new()` type constraint, but that
was relaxed back in 2019 with [this PR][relax-new-constraint-pr]. That change also showcases a supported
method for creating options instances: implementing `IOptionsFactory<T>`. What's more, that PR provided a
virtual method and base class so that our implementation only needs to implement a single method.

With a class like this:

```csharp
internal class LibSettingsOptionsFactory : OptionsFactory<LibSettings>
{
   public LibSettingsOptionsFactory(
    IEnumerable<IConfigureOptions<LibSettings>> setups,
    IEnumerable<IPostConfigureOptions<LibSettings>> postConfigures)
        : this(setups, postConfigures, validations: Array.Empty<IValidateOptions<LibSettings>>())
    {
    }

    public LibSettingsOptionsFactory(
        IEnumerable<IConfigureOptions<LibSettings>> setups,
        IEnumerable<IPostConfigureOptions<LibSettings>> postConfigures,
        IEnumerable<IValidateOptions<LibSettings>> validations)
        : base(setups, postConfigures, validations)
    {
    }

    protected override LibSettings CreateInstance(string name)
    {
        // Here's the whole implementation!
        return LibSettings.Create();
    }
}
```

and a corresponding extension method to simplify registration like this:

```csharp
public static class ServiceCollectionExtensions
{
    public static OptionsBuilder<LibSettings> AddLibSettings(this IServiceCollection services)
    {
        return services.AddLibSettings(Options.DefaultName);
    }

    public static OptionsBuilder<LibSettings> AddLibSettings(this IServiceCollection services, string name)
    {
        // Use TryAdd* so that this code respects any user overrides
        services.TryAddTransient<IOptionsFactory<LibSettings>, LibSettingsOptionsFactory>();
        return services.AddOptions<LibSettings>(name);
    }
}
```

I've implemented the IOptions pattern for a class without a parameterless constructor! Note that with
this little bit of code, LibSettings is now available as `IOptions<LibSettings>`,
`IOptionsMonitor<LibSettings>`, `IOptionsSnapshot<LibSettings>`, and is available via named instances as
well. Your settings can also hot-reload configuration changes if you use `IConfigureOptions<T>` as described
in [Andrew Lock's post][andrew-lock-iconfigureoptions].

Hopefully you can use this trick to make old or special libraries easier to consume from modern .NET code!

[IOptions pattern]: https://learn.microsoft.com/en-us/dotnet/core/extensions/options
[Options docs]: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options?view=aspnetcore-7.0#bind-hierarchical-configuration
[relax-new-constraint-pr]: https://github.com/dotnet/extensions/pull/2169/files
[andrew-lock-iconfigureoptions]: https://andrewlock.net/the-dangers-and-gotchas-of-using-scoped-services-when-configuring-options-in-asp-net-core/
