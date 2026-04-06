---
showHero: true
title: "Aspire in a Sandbox"
description: "Running a .NET Aspire dev environment inside a Gondolin micro-VM."
summary: "Running a .NET Aspire dev environment inside a Gondolin micro-VM."
date: 2026-04-05T00:00:00.000Z
tags:
 - ai
 - agents
 - aspire
 - dotnet
 - infrastructure
slug: "aspire-in-a-sandbox"
---

This is a follow-up to [Sandboxing the Eager Deputy][part-1], which makes the case for running AI agent code inside an
isolation boundary rather than trusting the agent to behave. This post is the hands-on companion: a .NET Aspire dev
environment running inside a [Gondolin][gondolin] micro-VM, with containers, network mediation, and host-accessible
services.

## What we're building

An Aspire AppHost that orchestrates an nginx container, with both the Aspire dashboard and nginx accessible from the
host through Gondolin's ingress gateway. The VM has no direct network access. NuGet packages and Docker images are
pulled through Gondolin's HTTPS-intercepting proxy, which enforces an explicit hostname allowlist. The project source is
mounted into the VM via a programmable filesystem layer. Together, it's a real development stack running end-to-end
inside a sandbox.

## Prerequisites

Gondolin runs on Linux, macOS, and WSL2 ([native Windows][gondolin-windows] is in progress) with either QEMU or
[libkrun][libkrun] as the VM backend. Everything here uses Ubuntu 24.04 under WSL2 with QEMU; adjust for your setup.

```bash
sudo apt install qemu-system-x86_64 cpio lz4 nodejs npm
```

You'll also need Zig 0.15.2 (for cross-compiling guest binaries when building custom images) and Docker or Podman on the
host (for building images). .NET is only needed inside the VM, not on the host.

### Enable KVM

Without KVM, QEMU falls back to software emulation and the difference is not subtle: `dotnet restore` took over 30
minutes in TCG mode and 30 seconds with KVM.

```bash
sudo usermod -aG kvm $USER
# Then from the host: wsl --shutdown, and reopen
```

### Build from native paths

If you're building custom Gondolin images from a Windows-mounted checkout, you'll hit errors like `AccessDenied` when
the Zig compiler tries to write to its cache under `/mnt/c/...`. Copy the source to a native ext4 path first
(e.g., `~/gondolin`).

## Step 1: Build a VM image with .NET and Docker

Gondolin's default Alpine image is minimal. We need .NET SDK 10 and Docker inside the VM, which means building a custom
image. Alpine 3.23 packages both `dotnet10-sdk` and Docker in its community repository, so everything installs at build
time with no extra steps.

Create `build-config.json`:

```json
{
  "arch": "x86_64",
  "distro": "alpine",
  "alpine": {
    "version": "3.23.0",
    "kernelPackage": "linux-virt",
    "kernelImage": "vmlinuz-virt",
    "rootfsPackages": [
      "linux-virt", "rng-tools", "bash", "ca-certificates", "curl",
      "openssh", "git", "dotnet10-sdk",
      "docker", "docker-cli", "containerd", "runc", "iptables"
    ],
    "initramfsPackages": []
  },
  "rootfs": {
    "label": "gondolin-root",
    "sizeMb": 4096
  },
  "init": {
    "rootfsInitExtra": "docker-init-extra.sh"
  }
}
```

The 4GB rootfs gives Docker room for pulled container images.

### The Docker init script

The `rootfsInitExtra` field points to `docker-init-extra.sh`, a shell script that runs at VM boot:

```sh
# Set up cgroup v2 for Docker container support
mkdir -p /sys/fs/cgroup 2>/dev/null || true
if ! grep -q " /sys/fs/cgroup " /proc/mounts; then
  mount -t cgroup2 cgroup2 /sys/fs/cgroup 2>/dev/null || true
fi

# Create runtime directories Docker expects
mkdir -p /var/run /var/lib/docker /run/docker
export PATH=/usr/local/bin:$PATH

# Enable IPv4 forwarding for Docker bridge networking
sysctl -w net.ipv4.ip_forward=1 >/dev/null 2>&1 || true

# Start dockerd with the VFS storage driver (overlayfs is
# not available in the minimal VM kernel)
if command -v dockerd > /dev/null 2>&1; then
  dockerd \
    --host=unix:///var/run/docker.sock \
    --exec-root=/run/docker \
    --data-root=/var/lib/docker \
    --storage-driver=vfs \
    --iptables=true \
    --ip-forward=true \
    --ip-masq=true \
    > /var/log/dockerd.log 2>&1 &
fi

# Poll until dockerd is ready (up to 6 seconds)
if command -v docker > /dev/null 2>&1; then
  i=0
  while [ $i -lt 60 ]; do
    if docker info > /dev/null 2>&1; then break; fi
    sleep 0.1
    i=$((i + 1))
  done
fi
```

This script runs under busybox `ash`, not bash, so stick to POSIX constructs. Every command must be safe to fail
(`|| true` or `2>/dev/null`); a non-zero exit kernel-panics the VM. The file must also have Unix line endings (LF); CRLF
fails the same way.

### Build and verify

```bash
gondolin build --config build-config.json --output ./assets

gondolin exec --image <build-id> -- dotnet --version
# 10.0.105

gondolin exec --image <build-id> -- docker version
# Docker Engine + Client
```

## Step 2: Create the Aspire AppHost

Using .NET 10's single-file app format:

```csharp
// apphost.cs
#:sdk Aspire.AppHost.Sdk@13.0.2

var builder = DistributedApplication.CreateBuilder(args);
builder.AddContainer("nginx", "nginx").WithHttpEndpoint(targetPort: 80);
builder.Build().Run();
```

One gotcha: Gondolin's ingress gateway connects to guest `127.0.0.1`, but on some configurations `localhost` resolves to
IPv6 `::1`, which the ingress can't reach. Bind to `0.0.0.0` explicitly. For single-file apps, create `apphost.run.json`
alongside the source file:

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "applicationUrl": "http://0.0.0.0:15194"
    }
  }
}
```

## Step 3: Configure the VM

```ts
import { VM, RealFSProvider, createHttpHooks } from "@earendil-works/gondolin";

const { httpHooks, env } = createHttpHooks({
  allowedHosts: [
    "*.nuget.org",                 // NuGet
    "*.docker.io",                 // Docker Hub auth + registry
    "*.cloudflare.docker.com",     // Docker Hub blob redirects
    "*.r2.cloudflarestorage.com",  // Docker Hub blob storage
    "*.microsoft.com",             // MCR, telemetry, SDK downloads
    "dotnetcli.azureedge.net",     // .NET SDK
  ],
});

const vm = await VM.create({
  httpHooks,
  env,
  memory: "4G",  // Default 1GB causes OOM during dotnet restore
  cpus: 4,
  sandbox: { imagePath: "./assets" },
  vfs: {
    mounts: {
      "/workspace": new RealFSProvider("./"),
      // Persist NuGet packages and Docker images across VM restarts.
      "/root/.nuget/packages": new RealFSProvider("./.nuget-cache"),
      "/var/lib/docker": new RealFSProvider("./.docker-cache"),
    },
  },
});
```

Docker Hub's pull flow redirects blob downloads to blob storage under hostnames like
`docker-images-prod.*.r2.cloudflarestorage.com`. Use `GONDOLIN_DEBUG=net` to see exactly which hostnames are needed for
your scenario.

### HTTPS interception

All HTTPS traffic from the VM goes through Gondolin's MITM proxy. The host generates a local CA certificate and makes it
available inside the guest at `/etc/gondolin/mitm/ca.crt`. The guest init scripts build a merged trust bundle so
standard tools (`curl`, `dotnet`, `docker`) trust the proxy automatically. Docker containers that need to make outbound
HTTPS calls will need the CA bundle mounted in; Gondolin's upstream [docker example][gondolin-docker-example] handles
this with a wrapper script.

## Step 4: Run

```ts
// Start Aspire
const proc = vm.exec(
  "cd /workspace && " +
  "ASPIRE_ALLOW_UNSECURED_TRANSPORT=true " +
  "DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true " +
  "dotnet run --file apphost.cs --launch-profile http",
  { stdout: "pipe", stderr: "pipe" },
);

// Wait for Kestrel to start accepting connections
for await (const chunk of proc.output()) {
  if (chunk.text.includes("Now listening")) break;
}
```

## Step 5: Expose via ingress

Gondolin's ingress gateway maps host HTTP requests to guest services using prefix-based routing. A request to
`/nginx/foo` on the host gets forwarded to the nginx port inside the VM as `/foo` (with the `/nginx` prefix stripped).
When multiple routes are defined, the longest matching prefix wins, so `/nginx` takes priority over `/` for requests
starting with `/nginx`.

```ts
const ingress = await vm.enableIngress({
  listenHost: "127.0.0.1",
  listenPort: 0,
});

// Route the Aspire dashboard
vm.setIngressRoutes([
  { prefix: "/", port: 15194, stripPrefix: false },
]);
console.log("Dashboard:", ingress.url);
```

Aspire assigns a dynamic port to the nginx container via DCP. Once the container is running, query its port and add an
ingress route:

```ts
const ports = await vm.exec("docker ps --format '{{.Ports}}'");
const portMatch = ports.stdout.match(/:(\d+)->80/);

if (portMatch) {
  const nginxPort = parseInt(portMatch[1]);
  vm.setIngressRoutes([
    { prefix: "/nginx", port: nginxPort, stripPrefix: true },
    { prefix: "/", port: 15194, stripPrefix: false },
  ]);
  console.log("nginx:", new URL("/nginx/", ingress.url).href);
}
```

> [!NOTE]
> At time of writing, the ingress gateway has a bug where it sends TCP FIN to the backend after forwarding the HTTP
> request, causing Kestrel to close without responding. I submitted a fix as
> [#84](https://github.com/earendil-works/gondolin/pull/84).

## What the sandbox actually does

From inside the VM, try reaching a host you haven't allowlisted:

```ts
const blocked = await vm.exec(
  "curl -s -o /dev/null -w '%{http_code}' https://evil.example.com"
);
// "000" - connection refused. The host never existed inside the VM's network.

const allowed = await vm.exec(
  "curl -s -o /dev/null -w '%{http_code}' https://api.nuget.org/v3/index.json"
);
// "200" - allowlisted, passes through the proxy.
```

The agent can restore packages, pull containers, and serve HTTP. It cannot exfiltrate data to an unapproved destination.
And the credentials it uses for approved destinations are injected at the proxy layer; they never exist inside the VM.

## Loosening the reins

The strict allowlist in this walkthrough is appropriate for running untrusted agent-generated code. But for day-to-day
development, you need Stack Overflow, package registries, and documentation sites. A locked-down allowlist would make
that miserable, and security tooling that makes developers miserable gets disabled.

Gondolin separates network access from credential access. Set `allowedHosts: ["*"]` and the agent can reach any host,
but secrets still only get injected for the specific destinations you've approved:

```ts
const { httpHooks, env } = createHttpHooks({
  allowedHosts: ["*"],
  secrets: {
    GITHUB_TOKEN: {
      hosts: ["api.github.com"],
      value: process.env.GITHUB_TOKEN,
    },
  },
});
```

The network is open, but the credentials are not. Inside the VM, `$GITHUB_TOKEN` contains a placeholder like
`GONDOLIN_SECRET_4eeaf8de...`. When the agent sends a request to `api.github.com` with that placeholder in the
`Authorization` header, the proxy substitutes the real token. Anywhere else, the placeholder goes through as-is. The
real token never enters the VM.

Secret injection is the core guarantee. The allowlist is defense in depth, and you can relax it without compromising
the credential boundary.

## Wiring it to an agent

[Copilot CLI][copilot-cli] has an extension system that can hook into the agent's lifecycle and intercept tool calls.
An extension can boot the VM on session start and rewrite shell commands to execute inside it:

```ts
// Conceptual sketch - not production code
import { execFile, execFileSync } from "node:child_process";
import { joinSession } from "@github/copilot-sdk/extension";

const isWindows = process.platform === "win32";

// Wrap gondolin CLI calls so they work on both platforms.
// On Windows, commands run inside WSL where Gondolin is installed.
// Once native Windows support lands (#21), this wrapper goes away
// and we can use the gondolin sdk directly.
function gondolin(...args) {
  if (isWindows) return execFileSync("wsl", ["-e", "gondolin", ...args], { encoding: "utf-8" });
  return execFileSync("gondolin", args, { encoding: "utf-8" });
}

let vmProcess;
let sessionSock;

const session = await joinSession({
  hooks: {
    onSessionStart: async () => {
      // Boot a persistent VM with the project mounted at /workspace.
      const vmArgs = [
        "bash",
        "--image", "./assets",
        "--mount-hostfs", `${process.cwd()}:/workspace`,
        "--allow-host", "*",
      ];

      vmProcess = isWindows
        ? execFile("wsl", ["-e", "gondolin", ...vmArgs])
        : execFile("gondolin", vmArgs);

      // Wait for the session to register, then find its socket.
      await new Promise(r => setTimeout(r, 5000));
      const list = gondolin("list");
      const match = list.match(/^(\S+)/m);
      sessionSock = `~/.cache/gondolin/sessions/${match[1]}.sock`;
    },

    onPreToolUse: async (input) => {
      if (input.toolName === "powershell" || input.toolName === "bash") {
        // Rewrite the command to execute inside the running VM.
        const execCmd = isWindows
          ? `wsl -e gondolin exec --sock ${sessionSock} -- ${input.toolArgs.command}`
          : `gondolin exec --sock ${sessionSock} -- ${input.toolArgs.command}`;
        return { modifiedArgs: { ...input.toolArgs, command: execCmd } };
      }
    },

    onSessionEnd: async () => {
      vmProcess?.kill();
    },
  },
});
```

The agent doesn't know it's sandboxed. It calls the same tools it always calls, and the extension rewrites the command
so shell commands execute inside the VM instead of on the host. This is the same pattern [pi-gondolin][pi-gondolin] uses
for the Pi coding agent.

The full integration (interactive sessions, file synchronization, port forwarding) is future work.

## Rough edges

The init script is the hardest part to get right. Errors manifest as "the VM didn't boot" with no useful feedback. The
only debugging tool is `GONDOLIN_DEBUG=protocol` and reading the kernel console output for clues. Once it works, it
works reliably, but the first iteration takes patience.

There's meaningful setup cost before you can use this day-to-day. Building a custom image, configuring allowlists,
writing the init script, wiring up the agent extension. This is infrastructure work, and it's front-loaded.

---

The cost buys structural enforcement: the agent never possesses the credentials, never controls the network policy, and
can't rewrite its own rules.

[part-1]: {{< ref "sandboxing-the-eager-deputy" >}}
[gondolin]: https://github.com/earendil-works/gondolin
[gondolin-windows]: https://github.com/earendil-works/gondolin/issues/21
[libkrun]: https://github.com/containers/libkrun
[gondolin-docker-example]: https://github.com/earendil-works/gondolin/blob/main/host/examples/docker-init-extra.sh
[copilot-cli]: https://github.com/github/copilot-cli
[pi-gondolin]: https://github.com/earendil-works/gondolin/blob/main/host/examples/pi-gondolin.ts
