---
title: "The curious case of PowerShell, Castle DynamicProxy and indexers"
excerpt: I seem to have hit some sort of edge case between the Castle DynamicProxy object and the binding / dispatching of method calls in PowerShell.
date: 2016-07-19T23:33:34.000Z
tags:
 - powershell
 - .net
slug: "powershell-dynamicproxy-indexers"
---

> UPDATE: This issue is now [tracked in uservoice][uservoice]. If it's affecting you, please upvote!

## Prerequisites

As I usually like to do, here's the prerequisites for the article. Instructions and techniques likely apply to other versions, but if you're having problems replicating my results, ensure your environment matches.

1. PowerShell 5 (comes with Windows 10)
2. [Castle DynamicProxy][castle-dynamicproxy] version 3.3.3 in your current working directory

## The Problem

I seem to have hit some sort of edge case between the Castle Project's [DynamicProxy][castle-dynamicproxy] object and the binding / dispatching of method calls in PowerShell. When an object with an [indexer][indexer] is proxied, that object's indexer is no longer available in PowerShell, however, the underlying `get_Item()` method is still available.

## The Setup

Here's a simple scenario to showcase the issue. First we need an interface that has an indexer in it, along with a simple class that implements that interface

```csharp
public interface MyInterface
{
    double this[int i] { get; }
}

public class MyClass : MyInterface
{
    private double[] _array = new [] { 1.1, 2.2 };
    public double this[int i]
    {
        get
        {
            return _array[i];
        }
    }
}
```

This is the object we ultimately care about and want to expose to the world. Next we need a DyanmicProxy `IInterceptor` to proxy the object

```csharp
public class MyProxy : IInterceptor
{
    public void Intercept(IInvocation invocation)
    {
        invocation.Proceed();
    }
}
```

This proxy does nothing but forward the call along, but it's enough to trigger the behavior. Lastly, for convenience, we create a factory to create the objects for us

```csharp
public static class MyFactory
{
    public static MyInterface GetViaInterface()
    {
        return new MyClass();
    }

    public static MyInterface GetViaProxy()
    {
        var generator = new ProxyGenerator();
        return (MyInterface)generator.CreateInterfaceProxyWithTarget(typeof(MyInterface), new MyClass(), new MyProxy());
    }
}
```

So now we can bring it all together with a small PowerShell script that does the following

1. Loads our classes into the AppDomain 
2. Creates an instance of `MyClass` three different ways
  1. Directly via `New-Object`
  2. As an instance of `MyInterface` via the factory
  3. As an instance of `MyInterface` wrapped with a proxy via the factory
3. Outputs the result of calling `$obj[1]` and `$obj.get_Item(1)` for each instance

Here's the script:

```powershell
$assemblies = (
    "Castle.Core, Version=3.3.0.0, Culture=neutral, PublicKeyToken=407dd0808d44fbdc"
)

$source = @"
using System;
using Castle.DynamicProxy;

namespace Sample
{
    public interface MyInterface
    {
        double this[int i] { get; }
    }

    public class MyClass : MyInterface
    {
        private double[] _array = new [] { 1.1, 2.2 };

        public double this[int i]
        {
            get
            {
                return _array[i];
            }
        }
    }
    
    public class MyProxy : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            invocation.Proceed();
        }
    }   

    public static class MyFactory
    {
        public static MyInterface GetViaInterface()
        {
            return new MyClass();
        }

        public static MyInterface GetViaProxy()
        {
            var generator = new ProxyGenerator();
            return (MyInterface)generator.CreateInterfaceProxyWithTarget(typeof(MyInterface), new MyClass(), new MyProxy());
        }
    }
}
"@

Add-Type -Path "$pwd\Castle.Core.dll"
Add-type -ReferencedAssemblies $assemblies -TypeDefinition $source -Language CSharp

function print
{
    param($Text, $Obj)
    
    $direct = $Obj[1]
    $item = $Obj.get_Item(1)
    
    echo "$Text --> `$Obj[1] = $direct `t `$Obj.get_Item(1) = $item"
}

print "Direct construction" (New-Object Sample.MyClass)
print "      Via interface" ([Sample.MyFactory]::GetViaInterface())
print "          Via proxy" ([Sample.MyFactory]::GetViaProxy())

```

which results in the following output

```
Direct construction --> $Obj[1] = 2.2    $Obj.get_Item(1) = 2.2
      Via interface --> $Obj[1] = 2.2    $Obj.get_Item(1) = 2.2
          Via proxy --> $Obj[1] =        $Obj.get_Item(1) = 2.2
```

Note that in the proxy case using the indexer directly gives no result, but using the underlying `get_Item()` method (which is how the indexer is actually implemented) works fine.

Here's the same sample setup in a C# console app

```csharp
using System;
using Castle.DynamicProxy;

namespace Sample
{
    public interface MyInterface
    {
        double this[int i] { get; }
    }

    public class MyClass : MyInterface
    {
        private double[] _array = new[] { 1.1, 2.2 };

        public double this[int i]
        {
            get
            {
                return _array[i];
            }
        }
    }

    public class MyProxy : IInterceptor
    {
        public void Intercept(IInvocation invocation)
        {
            invocation.Proceed();
        }
    }

    public static class MyFactory
    {
        public static MyInterface GetViaInterface()
        {
            return new MyClass();
        }
        public static MyInterface GetViaProxy()
        {
            var generator = new ProxyGenerator();
            return (MyInterface)generator.CreateInterfaceProxyWithTarget(typeof(MyInterface), new MyClass(), new MyProxy());
        }
    }

    public  class Program
    {
        private static void Print(string text, dynamic obj)
        {
            var direct = obj[1];
            var item = obj.get_Item(1);

            Console.WriteLine(string.Format("{0} --> obj[1] = {1} \t obj.Item(1) = {2}", text, direct, item));
        }

        static void Main(string[] args)
        {
            Print("Direct construction", new MyClass());
            Print("      Via interface", MyFactory.GetViaInterface());
            Print("          Via proxy", MyFactory.GetViaProxy());
            Console.ReadKey();
        }
    }
}
```

and the corresponding output

```
Direct construction --> obj[1] = 2.2     obj.Item(1) = 2.2
      Via interface --> obj[1] = 2.2     obj.Item(1) = 2.2
          Via proxy --> obj[1] = 2.2     obj.Item(1) = 2.2
```

In this case the indexer works as expected. Both samples try to run the same code, and the C# app uses the `dynamic` keyword to get the same late-binding used by PowerShell, but obviously something's different.


## Wrap Up

Of course as a workaround you can use `get_Item()` for now, but I'm hopeful for a [fix][uservoice]. Of course it's always possible my setup is incorrect and I have a bug, so if that's the case please let me know at [@MattKotsenas](https://twitter.com/MattKotsenas), or send a PR with fixes!


[castle-dynamicproxy]: http://www.castleproject.org/projects/dynamicproxy/
[indexer]: https://msdn.microsoft.com/en-us/library/6x16t2tx.aspx
[uservoice]: https://windowsserver.uservoice.com/forums/301869-powershell/suggestions/15425352--bug-castle-s-dynamicproxy-breaks-property-indexe?tracking_code=29201113a860fa7a2196756b8e488001