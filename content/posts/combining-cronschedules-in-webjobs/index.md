---
title: "Combining cron expressions in Azure WebJobs TimerTriggers"
cover: combining-cronschedules-in-webjobs.png
socialImage: /img/combining-cronschedules-in-webjobs-social.jpg
description: Azure supports using cron expressions to trigger WebJobs, but each job can have only a single cron schedule. Learn how to use the TimerSchedule class to create custom schedules and combine cron expressions
date: 2017-08-09T20:32:27.000Z
tags:
 - azure
 - webjobs
slug: "combining-cronschedules-in-webjobs"
---

Azure WebJobs support the concept of triggers, which let your job do work in response to a HTTP request, a blob or service bus message, or on a timer.
Specifically, the `TimerTrigger` attribute allows you to specify a cron expression as a trigger for your WebJob.
Rather than re-invent the wheel, I'll defer to the [README][timertrigger-readme] as a great resource for understanding using timers in your WebJob.

## The Problem

The TimerTrigger attribute is easy to use and super handy, but it's a bit limited in one important way: it can only handle a single cron expression.
Combining cron expressions can be very useful in situations where a job needs to run frequently sometimes (for example, during business hours), but infrequently at other times.

The simplest way to accomplish this is just to create multiple jobs that execute the same code, which might look something like this:

```csharp
// Every 15 minutes, between 06:00 AM and 08:59 PM
private const string PeakHours = "0 */15 6-20 * * *";

// Every hour from 12:00 AM to 06:00 AM and 09:00 PM to 12:00 AM
private const string NonPeakHours = "0 0 0-5,21-23 * * *";

public static void CleanupPeakHours([TimerTrigger(PeakHours)] TimerInfo timer)
{
    DoCleanup();
}

public static void CleanupNonPeakHours([TimerTrigger(NonPeakHours)] TimerInfo timer)
{
    DoCleanup();
}
```

But this means that we technically have _two_ WebJobs, and hence two sets of logs, and this solution just generally doesn't scale very well if you have lots of jobs or lots of cron expressions.

## Writing a custom TimerSchedule

The WebJobs SDK provides a [TimerSchedule][github-timerschedule] abstract class, which provides the building blocks we'll need to build a custom scheduler that supports multiple cron expressions, as well as reuse across jobs.
The extensions package provides a few implementations of `TimerSchedule`: `CronSchedule` and `ConstantSchedule`, which are designed to be used directly, as well as `DailySchedule` and `WeeklySchedule`, which are designed for extension.
Example are available [here][github-daily-weekly].

In our two-job solution, the `TimerTrigger` takes our cron expression and hands it off to `TimerSchedule` to parse it into an object and calculate the next occurrence of the schedule.

To inherit from `TimerSchedule`, we just need to implement `GetNextOccurrence()`:

```csharp
public class CombinedCronSchedule : TimerSchedule
{
    public override DateTime GetNextOccurrence(DateTime now)
    {
        throw new NotImplementedException();
    }
}
```

In order to implement `GetNextOccurrence`, we need:

1. A collection of cron expressions
2. For each expression, get the next occurrence of that expression
3. Pick the correct expression from the available list

which could look something like this:

```csharp
public class CombinedCronSchedule : TimerSchedule
{
    private readonly IReadOnlyCollection<CronSchedule> _schedules;

    public CombinedCronSchedule(params string[] expressions)
    {
        _schedules = expressions.Select(s => new CronSchedule(s)).ToList();
    }

    public override DateTime GetNextOccurrence(DateTime now)
    {
        return _schedules.Select(s => s.GetNextOccurrence(now)).Min();
    }
}
```

You can see that we delegate most of the work to `CronSchedule`.

That meets our needs, but from looking at the other implementations of `TimerSchedule`, we can make a few more minor tweaks to make debugging and extensibility easier.
Namely, we should override`ToString()`, and we should make the occurrence selector a parameter, instead of hardcoding it to be `Math.Min()`. The full solution then looks like this:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Extensions.Timers;

public class CombinedCronSchedule : TimerSchedule
{
    private readonly Func<IEnumerable<DateTime>, DateTime> _nextOccurenceSelector;
    private readonly IReadOnlyCollection<CronSchedule> _schedules;

    public CombinedCronSchedule(params string[] expressions) : this(dates => dates.Min(), expressions)
    {
    }

    public CombinedCronSchedule(Func<IEnumerable<DateTime>, DateTime> nextOccurenceSelector, params string[] expressions)
    {
        _nextOccurenceSelector = nextOccurenceSelector;
        _schedules = expressions.Select(s => new CronSchedule(s)).ToList();
    }

    public override DateTime GetNextOccurrence(DateTime now)
    {
        return _nextOccurenceSelector(_schedules.Select(s => s.GetNextOccurrence(now)));
    }

    public override string ToString()
    {
        var schedules = string.Join(", ", _schedules.Select(s => s.ToString()));
        return $"Schedules: {schedules}";
    }
}
```

So that's all we need to _support_ combining cron expressions, but now how do we _use_ it as our WebJob trigger?
The simplest way is to create a class that represents our new schedule:

```csharp
public class PeakNonPeakSchedule : CombinedCronSchedule
{
    // Every 15 minutes, between 06:00 AM and 08:59 PM
    private const string PeakHours = "0 */15 6-20 * * *";

    // Every hour from 12:00 AM to 06:00 AM and 09:00 PM to 12:00 AM
    private const string NonPeakHours = "0 0 0-5,21-23 * * *";

    public PeakNonPeakSchedule() : base(PeakHours, NonPeak)
    {
    }
}
```

(which can be reused for multiple jobs) and then to reference that schedule from the `TimerTrigger` attribute like this:

```csharp
public static void Cleanup([TimerTrigger(typeof(PeakNonPeakSchedule))] TimerInfo timer)
{
    DoCleanup();
}

// No second job needed!
```

## Caveats

Ideally, we could omit the `PeakNonPeakSchedule` class entirely and pass our expressions directly to `CombinedCronSchedule`, but two things prevent that:

1. `TimerTrigger` is an attribute, so its parameters must be compile-time constants, which means you can only pass a _type_, and that type requires a public, parameterless constructor
2. `TimerTrigger` is sealed, preventing us from creating our own attribute that uses `CombinedCronSchedule`

If anyone has suggestions on how to make the `CombinedCronSchedule` even easier to use, please let me know and I'll update the post.

A big thanks to [@petesopinions][petesopinions] for all his investigation and coming up with this solution!


[timertrigger-readme]: https://github.com/Azure/azure-webjobs-sdk-extensions/blob/master/README.md#timertrigger
[github-constantschedule]: https://github.com/Azure/azure-webjobs-sdk-extensions/blob/master/src/WebJobs.Extensions/Extensions/Timers/Scheduling/ConstantSchedule.cs
[github-daily-weekly]: https://github.com/Azure/azure-webjobs-sdk-extensions/blob/master/src/ExtensionsSample/Samples/TimerSamples.cs
[petesopinions]: https://twitter.com/petesopinions