---
title: "Testing in Production with .NET Proxies"
isPost: true
active: true
excerpt: ???
postDate: '2016-01-28 22:00:54 GMT-0800'
tags:
 - csharp
---
Part 1
  - What is testing in production / verification
  - aka branching by abstraction (with verification)
    - In this case we're focusing on the verification part
    - http://www.alwaysagileconsulting.com/articles/application-pattern-verify-branch-by-abstraction/

  - What is the proxy pattern
    - How does the proxy pattern support SRP, open close
    
  - Simple example
    - RealProxy that does logging directly
    
  - This is AOP
    - http://www.castleproject.org/projects/dynamicproxy/
    - https://www.postsharp.net/

Part 2
  - Really using a RealProxy
    - Limitiations regarding ContextBoundObject or interface
      - Since your branching by abstraction, you already have an interface
    - http://blogs.msdn.com/b/cbrumme/archive/2003/07/14/51495.aspx
    
  - A reusable RealProxy base --> ReflectingProxy
  - Sample proxies (with tests?)
    - TimingProxy
    - ExceptionSwallowingProxy
    - ComparingProxy
      - This needs more explanation?
        - Why and how to use it
        - You can chain it

Part 3
  - Performance impact of reflection
  - Using DispatchProxy moving forward
    - https://github.com/dotnet/corefx/tree/master/src/System.Reflection.DispatchProxy