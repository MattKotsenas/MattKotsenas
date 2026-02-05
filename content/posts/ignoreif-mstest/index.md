---
title: "Programmatically skip / ignore tests in MSTest v2"
cover: cover.png
socialImage: cover-social.jpg
description: Create a custom attribute to extend MSTest to programmatically skip / ignore tests based on factors like OS or framework version
date: 2019-03-20T17:38:20.000Z
tags:
 - dotnet
 - mstest
 - testfx
 - testing
slug: "ignoreif-mstest"
---

If you read that headline and you're like me, your first thought is probably "that sounds like a terrible idea". While
usually I'd agree with you (and myself?), allow me to bury the lede a little bit and provide a bit of motivation. Then,
with that out of the way, I'll describe one possible solution to a problem that occurs more often than you might expect.

## The Motivation

Recently I've had to write some .NET Core code that runs cross-platform (Windows, Linux, and macOS).
Unfortunately for me, a fair amount of that code interacts with the underlying OS in various ways (OS APIs, file
system manipulation, etc.) that .NET doesn't completely abstract away. As a result, I'm writing platform-specific code.

Here's an example for the purposes of this post:

```csharp
public class PathHelper
{
    public bool IsSamePath(string path1, string path2)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return string.Equals(path1, path2, StringComparison.OrdinalIgnoreCase);
        }
        return string.Equals(path1, path2, StringComparison.Ordinal);
    }
}
```

In this example, I have a "helper class" (I'm not a big fan of classes with the name "helper" in it, but I'll save
that for another day) that takes two paths as strings and decides if they represent the same path. This method is aware
that on Windows, paths are by default [case-insensitive][winnt-case-insensitive], whereas on Linux and macOS, paths are case sensitive, and
adjusts accordingly (keep in mind this is just an example, in reality Windows can be made case sensitive, and this
code doesn't handle lots of cases you should care about like canonicalization, along with other problems).

Now, of course, since we're all good little engineers, there's also a test that goes with this code (you _do_ write
tests, don't you?). I use the [MSTest v2 framework][testfx] just to keep friction low for other developers.

```csharp
[TestClass]
public class TwoPathsThatDifferOnlyByCase
{
    [TestMethod]
    public void AreTheSameOnWindowsAndDifferentOnLinuxAndMacOS()
    {
        var pathHelper = new PathHelper();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.IsTrue(pathHelper.IsSamePath("C:\\HELLO\\WORLD", "C:\\hello\\world"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.IsFalse(pathHelper.IsSamePath("/HELLO/WORLD", "/hello/world"));
        }
        else
        {
            Assert.Fail("Unknown OS Platform");
        }
    }
}
```

So that gets the job done, but I have to say looking at it makes me feel a bit... bad. There are a couple of
problems I see with our test case (our example code suffers from similar problems, but let's just focus on tests for now):

* The test must be run on multiple platforms to hit all the interesting assertions, which isn't obvious without looking at the code. If a developer runs the tests in Visual Studio and gets all green check marks, that should mean we're good to go, right?
* By using a [Behavior Driven Development][bdd]-esque naming scheme, the name make it clear that the test is doing too much (I mean, who names something "AreTheSameOnWindowsAndDifferentOnLinuxAndMacOS"?)
* Because Windows and Linux file systems are different, we shouldn't reuse the test data; on one platform a rooted path might start `C:\` whereas on another it might start `/`.

Really these are two different but related tests that just happen to be combined because that's the way the code is structured. So how can we make it better?

## What's an ideal solution?

In my ideal world, we could break this test apart into two tests: one for Windows and one for Linux and macOS.
Here's an example of what I mean:

```csharp
[TestClass]
public class TwoPathsThatDifferOnlyByCase
{
    [TestMethod] // This test should only run on Windows
    public void AreTheSameOnWindows()
    {
        var pathHelper = new PathHelper();
        Assert.IsTrue(pathHelper.IsSamePath("C:\\HELLOWORLD", "C:\\helloworld"));
    }

    [TestMethod] // This test should only run on Linux and macOS
    public void AreDifferentOnLinuxAndMacOS()
    {
        var pathHelper = new PathHelper();
        Assert.IsFalse(pathHelper.IsSamePath("/HELLO/WORLD", "/hello/world"));
    }
}
```

Breaking them apart would clearly signal to developers that there are two test cases here to consider and allow them to pass / fail independently.
Of course, simply creating two test cases causes an immediate problem: one will always fail. Developers working on Windows will always see a failing
Linux test and vice versa. That's a big no-no in my book, because it starts to reinforce the idea that it's OK to have broken tests.

The "official" way to solve this in MSTest v2 is to use the `[TestCategory]` [attribute][testcategory-mstest] and use names like "windows-only"
and "linux-only" and then filter test cases out that way. Other test frameworks like xUnit and NUnit have similar mechanisms via
[traits][testcategory-xunit] and [categories][testcategory-nunit]. However, that requires developers to configure the IDE properly, is fragile
as it's based on strings, and can quickly become unwieldy.

It would be better if a test could declare its dependencies and the test runner could evaluate those and decide whether to run the test.
Something like the `[Ignore]` attribute, but dynamically / conditionally applied at runtime...

## Introducing the [IgnoreIf] Attribute

So now with all that out of the way, I'd like to propose one possible way to solve this, at least for MSTest; I'm sure other test
frameworks can use similar solutions (or already do).

Let's introduce a new attribute `[IgnoreIf]`, that lets us do just that by specifying the name of a method to run to evaluate if
the test should run:

```csharp
[TestClass]
public class TwoPathsThatDifferOnlyByCase
{
    private static bool NotWindows()
    {
        return !RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    private static bool NotLinuxNorMacOS()
    {
        return !RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
               !RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    [TestMethodWithIgnoreIfSupport]
    [IgnoreIf(nameof(NotWindows))]
    public void AreTheSameOnWindows()
    {
        var pathHelper = new PathHelper();
        Assert.IsTrue(pathHelper.IsSamePath("C:\\HELLOWORLD", "C:\\helloworld"));
    }

    [TestMethodWithIgnoreIfSupport]
    [IgnoreIf(nameof(NotLinuxNorMacOS))]
    public void AreDifferentOnLinuxAndMacOS()
    {
        var pathHelper = new PathHelper();
        Assert.IsFalse(pathHelper.IsSamePath("/HELLO/WORLD", "/hello/world"););
    }
}
```

MSTest has several extensibility points that you're probably aware of such as `[TestInitalize]` and `[TestCleanup]`, but those
are aimed at setting up _your_ environment for testing. In this case, we want to modify test execution. Lucky for us, the
MSTest team has started [thinking about this][testfx-docs] and has two extensibility points already: subclassing the `[TestClass]`
and `[TestMethod]` attributes.

Unfortunately for us, the `[Ignore]` attribute is marked as `sealed` and doesn't have any extensibility points, so we must create
one using the `[TestMethod]`.

For this solution, I borrowed from the MSTest extensibility document directly, as well as [Gerald Barre's post][meziantou] on using test
method extensibility to set the thread's apartment state. In our case, we want our test method to know about our `[IgnoreIf]`
attribute, invoke the referenced method, and interpret the result.

```csharp
/// <summary>
/// An extension to the [TestMethod] attribute. It walks the method and class hierarchy looking
/// for an [IgnoreIf] attribute. If one or more are found, they are each evaluated, if the attribute
/// returns `true`, evaluation is short-circuited, and the test method is skipped.
/// </summary>
public class TestMethodWithIgnoreIfSupportAttribute : TestMethodAttribute
{
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        var ignoreAttributes = FindAttributes(testMethod);

        // Evaluate each attribute, and skip if one returns `true`
        foreach (var ignoreAttribute in ignoreAttributes)
        {
            if (ignoreAttribute.ShouldIgnore(testMethod))
            {
                var message = $"Test not executed. Conditional ignore method '{ignoreAttribute.IgnoreCriteriaMethodName}' evaluated to 'true'.";
                return new[]
                {
                    new TestResult
                    {
                        Outcome = UnitTestOutcome.Inconclusive,
                        TestFailureException = new AssertInconclusiveException(message)
                    }
                };
            }
        }
        return base.Execute(testMethod);
    }

    private IEnumerable<IgnoreIfAttribute> FindAttributes(ITestMethod testMethod)
    {
        // Look for an [IgnoreIf] on the method, including any virtuals this method overrides
        var ignoreAttributes = new List<IgnoreIfAttribute>();
        ignoreAttributes.AddRange(testMethod.GetAttributes<IgnoreIfAttribute>(inherit: true));

        // Walk the class hierarchy looking for an [IgnoreIf] attribute
        var type = testMethod.MethodInfo.DeclaringType;
        while (type != null)
        {
            ignoreAttributes.AddRange(type.GetCustomAttributes<IgnoreIfAttribute>(inherit: true));
            type = type.DeclaringType;
        }
        return ignoreAttributes;
    }
}
```

So here we've named our new attribute `TestMethodWithIgnoreIfSupportAttribute` to be descriptive (feel free to name
it something like `TestMethodEx` if the name is too long). The `Execute` method looks for our `[IgnoreIf]` attributes up the
class hierarchy, and if an ignore returns `true`, we stop processing, don't run the test, and instead return a `TestResult`
saying that the test wasn't run. This outcome also results in the little yellow triangle in Visual Studio, just like you get
with the regular `[Ignore]`.

![image of Visual Studio Test Explorer pain with two tests, one named "AreTheSameOnWindows" with the passed icon next to it, and one named "AreNotTheSameOnLinuxAndMacOS" with the skipped icon next to it][test-explorer]

Now that we have our extension point for the `[IgnoreIf]` attribute, it's time to implement it:

```csharp
/// <summary>
/// An extension to the [Ignore] attribute. Instead of using test lists / test categories to conditionally
/// skip tests, allow a [TestClass] or [TestMethod] to specify a method to run. If the method returns
/// `true` the test method will be skipped. The "ignore criteria" method must be `static`, return a single
/// `bool` value, and not accept any parameters. By default, it is named "IgnoreIf".
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class IgnoreIfAttribute : Attribute
{
    public string IgnoreCriteriaMethodName { get; }

    public IgnoreIfAttribute(string ignoreCriteriaMethodName = "IgnoreIf")
    {
        IgnoreCriteriaMethodName = ignoreCriteriaMethodName;
    }

    internal bool ShouldIgnore(ITestMethod testMethod)
    {
        try
        {
            // Search for the method specified by name in this class or any parent classes.
            var searchFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy | BindingFlags.Static;
            var method = testMethod.MethodInfo.DeclaringType.GetMethod(IgnoreCriteriaMethodName, searchFlags);
            return (bool) method.Invoke(null, null);
        }
        catch (Exception e)
        {
            var message = $"Conditional ignore method {IgnoreCriteriaMethodName} not found. Ensure the method is in the same class as the test method, marked as `static`, returns a `bool`, and doesn't accept any parameters.";
            throw new ArgumentException(message, e);
        }
    }
}
```

The attribute itself is simple, with just a single, reflection-heavy operation that gets the "ignore criteria" method from the name,
executes it, and returns the result.

One final piece, and this is totally optional. We can also create a custom `[TestClass]` attribute so that a whole test class can be
annotated with `[IgnoreIf]` criteria, instead of requiring our `TestMethodWithIgnoreIfSupport` be used in place of every `[TestMethod]`.

The code for that simply looks like this:

```csharp
/// <summary>
/// An extension of the [TestClass] attribute. If applied to a class, any [TestMethod] attributes
/// are automatically upgraded to [TestMethodWithIgnoreIfSupport].
/// </summary>
public class TestClassWithIgnoreIfSupportAttribute : TestClassAttribute
{
    public override TestMethodAttribute GetTestMethodAttribute(TestMethodAttribute testMethodAttribute)
    {
        if (testMethodAttribute is TestMethodWithIgnoreIfSupportAttribute)
        {
            return testMethodAttribute;
        }
        return new TestMethodWithIgnoreIfSupportAttribute();
    }
}
```

Also note that after writing this post, I found that a snippet called `[ConditionalFact]` for xUnit seems to be [floating around][conditionalfact-github-search].

[winnt-case-insensitive]: https://devblogs.microsoft.com/commandline/per-directory-case-sensitivity-and-wsl/
[bdd]: https://medium.com/@TechMagic/get-started-with-behavior-driven-development-ecdca40e827b
[testcategory-mstest]: https://visualstudiomagazine.com/blogs/tool-tracker/2018/07/organizing-test-cases.aspx
[testcategory-xunit]: https://www.brendanconnolly.net/organizing-tests-with-xunit-traits/
[testcategory-nunit]: https://docs.nunit.org/articles/nunit/writing-tests/attributes/category.html
[testfx]: https://github.com/Microsoft/testfx
[testfx-docs]: https://github.com/Microsoft/testfx-docs/tree/master/RFCs
[meziantou]: https://www.meziantou.net/2018/02/26/mstest-v2-customize-test-execution
[conditionalfact-github-search]: https://github.com/search?l=C%23&q=ConditionalFactAttribute&type=Code
[test-explorer]: test-explorer.png
