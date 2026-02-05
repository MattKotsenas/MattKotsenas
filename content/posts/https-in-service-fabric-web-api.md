---
title: "HTTP & HTTPS in Service Fabric Web API"
excerpt: Updating the boilerplate code in a Service Fabric Web API to support HTTP and HTTPS endpoints simultaneously
date: 2016-04-27T01:19:19.000Z
tags:
 - azure
 - service fabric
 - dotnet
slug: "https-in-service-fabric-web-api"
---

## Prerequisites

Before we start, here are the prerequisites for this article. These instructions were written and tested against the following versions of the Service Fabric API, though the code and techniques likely apply to other versions as well.

1. Service Fabric SDK 2.0.135 and Runtime 5.0.135 installed from [here][dev-env]
2. A Web API service based on [OWIN self-hosting][owin-service], which you can create by adding a new Web API service template from Visual Studio

## Getting Started

If you're creating your first Web API microservice in Service Fabric, you [probably][why-https] want to add HTTPS support pretty early on. In my not-so-humble experience getting the boilerplate `ValuesController` 
in the new project accessible over HTTPS took a lot longer than I expected, so here's the approach I took. Hopefully this advice will ease the pain for you and also make your code more flexible in the future.

```csharp
public class ValuesController : ApiController
{
    // GET api/values
    public IEnumerable<string> Get()
    {
        return new string[] { "value1", "value2" };
    }

    // GET api/values/5
    public string Get(int id)
    {
        return "value";
    }

    // POST api/values
    public void Post([FromBody]string value)
    {
    }

    // PUT api/values/5
    public void Put(int id, [FromBody]string value)
    {
    }

    // DELETE api/values/5
    public void Delete(int id)
    {
    }
}
```
*The 'Hello World' controller for new Web API projects*

### Step 1: Add an HTTPS endpoint to the Service and Application manifests

##### Make sure you have a certificate in your Service Fabric cluster

If you followed the [Secure a Service Fabric cluster docs][secure-a-fabric-cluster] then you already have the management certificate that you can reuse for this purpose. If your cluster is insecure, then go back and fix that first!

##### Add the certificate to the LocalMachine\My store on your dev machine for debugging

According to [the docs][secure-a-fabric-cluster] the certificate should be added to `CurrentUser\My` and `CurrentUser\TrustedPeople`, however I also had to add the certificate to `LocalMachine\My` in order to debug on the local cluster. This is because the Service Fabric local cluster runs as **NETWORK SERVICE** and not as your user account. The PowerShell command to import the certificate is

```powershell
Import-PfxCertificate -Exportable -CertStoreLocation Cert:\LocalMachine\My -FilePath C:\path\to\cert.pfx -Password (Read-Host -AsSecureString -Prompt "Enter Certificate Password")
```

> NOTE: I think this isn't part of the MSDN setup because it's possible your cluster won't ever open HTTPS ports (e.g. uses only queues, binary protocols, etc.).
> Thus I'm not sure where this should go in the docs. If you find a good place for it, let me know!

##### Update ServiceManifest.xml and ApplicationManifest.xml

[Follow the instructions][endpoint-manifest-docs] on updating your `ServiceManifest.xml` and `ApplicationManifest.xml`. This tells the Service Fabric cluster that you intend to open an HTTPS port and to ensure the SSL certificate is available. After you're done your manifests' diffs should look similar to this

```diff
diff --git a/SampleWebApi/PackageRoot/ServiceManifest.xml b/SampleWebApi/PackageRoot/ServiceManifest.xml
index b6d1453..4fce186 100644
--- a/SampleWebApi/PackageRoot/ServiceManifest.xml
+++ b/SampleWebApi/PackageRoot/ServiceManifest.xml
@@ -29,6 +29,7 @@
            listen. Please note that if your service is partitioned, this port is shared with
            replicas of different partitions that are placed in your code. -->
             <Endpoint Protocol="http" Name="ServiceEndpoint" Type="Input" Port="8521" />
+            <Endpoint Protocol="https" Name="ServiceEndpointHttps" Type="Input" Port="443" />
         </Endpoints>
     </Resources>
 </ServiceManifest>
\ No newline at end of file
diff --git a/SampleServiceFabric/ApplicationPackageRoot/ApplicationManifest.xml b/SampleServiceFabric/ApplicationPackageRoot/ApplicationManifest.xml
index 145b554..2ab00c2 100644
--- a/SampleServiceFabric/ApplicationPackageRoot/ApplicationManifest.xml
+++ b/SampleServiceFabric/ApplicationPackageRoot/ApplicationManifest.xml
@@ -6,6 +6,9 @@
    <ServiceManifestImport>
       <ServiceManifestRef ServiceManifestName="SampleWebApiPkg" ServiceManifestVersion="1.0.0" />
       <ConfigOverrides />
+      <Policies>
+         <EndpointBindingPolicy EndpointRef="ServiceEndpointHttps" CertificateRef="MyCert" />
+      </Policies>
    </ServiceManifestImport>
    <DefaultServices>
       <Service Name="SampleWebApi">
@@ -14,4 +17,7 @@
          </StatelessService>
       </Service>
    </DefaultServices>
+   <Certificates>
+      <EndpointCertificate X509FindValue="1234567890ABCDEFEDCBA0987654321AABBCCDDE" Name="MyCert" />
+   </Certificates>
 </ApplicationManifest>
\ No newline at end of file

```

##### Open the Load Balancer port(s)

Navigate to your Azure Load Balancer (it was created automatically in the same resource group as your Service Fabric cluster) and add two new **Load balancing rules** and **Probes** if they don't already exist

<table>
  <theader>
    <th>Probe / LB Rule</th>
    <th>Name</th>
    <th>Port</th>
  </theader>
  <tbody>
    <tr><td>Probe</td><td>WebApiHttp</td><td>HTTP on port 8521</td></tr>
    <tr><td>Probe</td><td>WebApiHttps</td><td>TCP on port 443</td></tr>
    <tr><td>LB Rule</td><td>WebApiHttp</td><td>TCP from port 80 to 8521</td></tr>
    <tr><td>LB Rule</td><td>WebApiHttps</td><td>TCP from port 443 to 443</td></tr>
  </tbody>
</table>

![Azure Load Balancer HTTPS rule and probe][img-lb]

### Step 2: Update OwinCommunicationListener

Now we've got two service endpoints in our manifest. We're done, right? Unfortunately, if you look at the Diagnostic Events for your service you should see logs similar to this

<table>
  <theader>
    <th>Timestamp</th>
    <th>Event Name</th>
    <th>Message</th>
  </theader>
  <tbody>
    <tr><td>18:47:57.040</td><td>StatelessRunAsyncCompletion</td><td>RunAsync has successfully completed for a stateless service instance</td></tr>
    <tr><td>18:47:57.032</td><td>StatelessRunAsyncInvocation</td><td>RunAsync has been invoked for a stateless service instance</td></tr>
    <tr><td>18:47:57.008</td><td>ServiceMessage</td><td>Listening on Http://10.0.0.4:8521/</td></tr>
    <tr><td>18:47:56.003</td><td>ServiceMessage</td><td>Starting web server on Http://+:8521/</td></tr>
    <tr><td>18:47:55.904</td><td>ServiceTypeRegistered</td><td>Service host process 4472 register service type</td></tr>
  </tbody>
</table>

Look like the service is still only listening for on the HTTP endpoint. If you jump to `OwinCommunicationListener::OpenAsync()` you should see code that looks like this

```csharp
public Task<string> OpenAsync(CancellationToken cancellationToken)
{
    var serviceEndpoint = this.serviceContext.CodePackageActivationContext.GetEndpoint(this.endpointName);
    int port = serviceEndpoint.Port;

    if (this.serviceContext is StatefulServiceContext)
    {
        StatefulServiceContext statefulServiceContext = this.serviceContext as StatefulServiceContext;
        this.listeningAddress = string.Format(
            CultureInfo.InvariantCulture,
            "http://+:{0}/{1}{2}/{3}/{4}",
            port,
            string.IsNullOrWhiteSpace(this.appRoot)
                ? string.Empty
                : this.appRoot.TrimEnd('/') + '/',
            statefulServiceContext.PartitionId,
            statefulServiceContext.ReplicaId,
            Guid.NewGuid());
    }
    else if (this.serviceContext is StatelessServiceContext)
    {
        this.listeningAddress = string.Format(
            CultureInfo.InvariantCulture,
            "http://+:{0}/{1}",
            port,
            string.IsNullOrWhiteSpace(this.appRoot)
                ? string.Empty
                : this.appRoot.TrimEnd('/') + '/');
    }
    else
    {
        throw new InvalidOperationException();
    }

    this.publishAddress = this.listeningAddress.Replace("+", FabricRuntime.GetNodeContext().IPAddressOrFQDN);

    try
    {
        this.eventSource.ServiceMessage(this.serviceContext, "Starting web server on " + this.listeningAddress);
        this.webApp = WebApp.Start(this.listeningAddress, appBuilder => this.startup.Invoke(appBuilder));
        this.eventSource.ServiceMessage(this.serviceContext, "Listening on " + this.publishAddress);
        return Task.FromResult(this.publishAddress);
    }
    catch (Exception ex)
    {
        this.eventSource.ServiceMessage(this.serviceContext, "Web server failed to open. " + ex.ToString());
        this.StopWebServer();
        throw;
    }
}
```

Interestingly, the `this.listeningAddress` has "http" hardcoded! We can update this code to pull the protocol from the endpoint definition in the manifest, and while we're at it, let's add some additional logging which will come in handy later. The diff should look like this

```diff
diff --git a/SampleWebApi/OwinCommunicationListener.cs b/SampleWebApi/OwinCommunicationListener.cs
index 9b5d2ad..f849da4 100644
--- a/SampleWebApi/OwinCommunicationListener.cs
+++ b/SampleWebApi/OwinCommunicationListener.cs
@@ -59,16 +59,22 @@ namespace SampleWebApi
 
         public Task<string> OpenAsync(CancellationToken cancellationToken)
         {
+            this.eventSource.ServiceMessage(this.serviceContext, "Calling OpenAsync on endpoint {0}", this.endpointName);
+
             var serviceEndpoint = this.serviceContext.CodePackageActivationContext.GetEndpoint(this.endpointName);
+            var protocol = serviceEndpoint.Protocol;
             int port = serviceEndpoint.Port;
 
+            this.eventSource.ServiceMessage(this.serviceContext, "Found endpoint with protocol '{0}' port '{1}'", protocol, port);
+
             if (this.serviceContext is StatefulServiceContext)
             {
                 StatefulServiceContext statefulServiceContext = this.serviceContext as StatefulServiceContext;
 
                 this.listeningAddress = string.Format(
                     CultureInfo.InvariantCulture,
-                    "http://+:{0}/{1}{2}/{3}/{4}",
+                    "{0}://+:{1}/{2}{3}/{4}/{5}",
+                    protocol,
                     port,
                     string.IsNullOrWhiteSpace(this.appRoot)
                         ? string.Empty
@@ -81,7 +87,8 @@ namespace SampleWebApi
             {
                 this.listeningAddress = string.Format(
                     CultureInfo.InvariantCulture,
-                    "http://+:{0}/{1}",
+                    "{0}://+:{1}/{2}",
+                    protocol,
                     port,
                     string.IsNullOrWhiteSpace(this.appRoot)
                         ? string.Empty
@@ -106,7 +113,7 @@ namespace SampleWebApi
             }
             catch (Exception ex)
             {
-                this.eventSource.ServiceMessage(this.serviceContext, "Web server failed to open. " + ex.ToString());
+                this.eventSource.ServiceMessage(this.serviceContext, "Web server for endpoint {0} failed to open. {1}", this.endpointName, ex.ToString());
 
                 this.StopWebServer();
 
@@ -116,7 +123,7 @@ namespace SampleWebApi
 
         public Task CloseAsync(CancellationToken cancellationToken)
         {
-            this.eventSource.ServiceMessage(this.serviceContext, "Closing web server");
+            this.eventSource.ServiceMessage(this.serviceContext, "Closing web server for endpoint {0}", this.endpointName);
 
             this.StopWebServer();
 
@@ -125,7 +132,7 @@ namespace SampleWebApi
 
         public void Abort()
         {
-            this.eventSource.ServiceMessage(this.serviceContext, "Aborting web server");
+            this.eventSource.ServiceMessage(this.serviceContext, "Aborting web server for endpoint {0}", this.endpointName);
 
             this.StopWebServer();
         }
```


### Step 3: Update CreateServiceInstanceListeners()

OK, now we're done, right? Not quite. If you debug your service you'll again see that the `OwinCommunicationListener` is only attempting to listen on the **ServiceEndpoint**, not on our new **ServiceEndpointHttps**. After much fruitless debugging I stumbled upon the code in `SampleWebApi` (or whatever your `StatelessService` is called). Take a look at `CreateServiceInstanceListeners()`

```csharp
/// <summary>
/// Optional override to create listeners (like tcp, http) for this service instance.
/// </summary>
/// <returns>The collection of listeners.</returns>
protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
{
    return new[]
    {
        new ServiceInstanceListener(serviceContext => new OwinCommunicationListener(Startup.ConfigureApp, serviceContext, ServiceEventSource.Current, "ServiceEndpoint"))
    };
}
```
Again with the hardcoding! This time we're assuming there's an endpoint in `ServiceManifest.xml` named "ServiceEndpoint". Let's fix this up again so that we create a listener for each endpoint

```diff
diff --git a/SampleWebApi/SampleWebApi.cs b/SampleWebApi/SampleWebApi.cs
index 18137a6..55a7866 100644
--- a/SampleWebApi/SampleWebApi.cs
+++ b/SampleWebApi/SampleWebApi.cs
@@ -1,5 +1,8 @@
-using System.Collections.Generic;
+using System;
+using System.Collections.Generic;
 using System.Fabric;
+using System.Fabric.Description;
+using System.Linq;
 using Microsoft.ServiceFabric.Services.Communication.Runtime;
 using Microsoft.ServiceFabric.Services.Runtime;
 
@@ -20,10 +23,12 @@ namespace SampleWebApi
         /// <returns>The collection of listeners.</returns>
         protected override IEnumerable<ServiceInstanceListener> CreateServiceInstanceListeners()
         {
-            return new[]
-            {
-                new ServiceInstanceListener(serviceContext => new OwinCommunicationListener(Startup.ConfigureApp, serviceContext, ServiceEventSource.Current, "ServiceEndpoint"))
-            };
+            var endpoints = Context.CodePackageActivationContext.GetEndpoints()
+                                   .Where(endpoint => endpoint.Protocol == EndpointProtocol.Http || endpoint.Protocol == EndpointProtocol.Https)
+                                   .Select(endpoint => endpoint.Name);
+
+            return endpoints.Select(endpoint => new ServiceInstanceListener(
+                serviceContext => new OwinCommunicationListener(Startup.ConfigureApp, serviceContext, ServiceEventSource.Current, endpoint), endpoint));
         }
     }
 }
```
This creates an endpoint for each HTTP / HTTPS endpoint found in the manifest. Additionally we name the `ServiceInstanceListener` the name of the endpoint since by default it has a blank name, and each listener must have a unique name.

## Wrap Up

Phew! That should do it. Your Web API service should now be available over both HTTP and HTTPS for both local debugging and in the Service Fabric cluster.

I'm new to Service Fabric myself, so it's possible I've done something boneheaded in the steps. If so, feel free to reach out to me at [@MattKotsenas](https://twitter.com/MattKotsenas), or send a PR with fixes!

## Pull Requests

Just for funsies, here are the Pull Requests opened while developing this article

- [[PR 6405]][pr-6405] Fix typos in Import-PfxCertificate calls in service-fabric-cluster-security
- [[PR 6415]][pr-6415] Update OwinCommunicationListener to support HTTP or HTTPS endpoints
- [[PR 6416]][pr-6416] Log endpoint name in OwinCommunicationListener
- [[PR 6417]][pr-6417] Remove hardcoded endpoint names in CreateServiceInstanceListeners()

[dev-env]: https://azure.microsoft.com/en-us/documentation/articles/service-fabric-get-started/
[owin-service]: https://azure.microsoft.com/en-us/documentation/articles/service-fabric-reliable-services-communication-webapi/
[why-https]: https://snyk.io/blog/10-reasons-to-use-https/
[secure-a-fabric-cluster]: https://azure.microsoft.com/en-us/documentation/articles/service-fabric-cluster-security/
[endpoint-manifest-docs]: https://azure.microsoft.com/en-us/documentation/articles/service-fabric-service-manifest-resources/
[img-lb]: /img/https-in-service-fabric-web-api/load-balancer-port.png "Azure Load Balancer HTTPS rule and probe"

[pr-6405]: https://github.com/Azure/azure-content/pull/6405
[pr-6415]: https://github.com/Azure/azure-content/pull/6415
[pr-6416]: https://github.com/Azure/azure-content/pull/6416
[pr-6417]: https://github.com/Azure/azure-content/pull/6417