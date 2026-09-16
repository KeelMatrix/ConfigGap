# ConfigGap Phase 0 feasibility evidence

## Verdict

**NARROW.** The probe has 100.00% blocking precision and zero dynamic blocking findings on the evaluated corpus, but measured static-key recall is 55.17%, below the specification's 90% gate. The recall result is also limited by project assets not being available for several legacy public repositories; the restore-capable harness remains available, but its all-corpus validation did not reach analysis after stalling during the first legacy restore. No product implementation should proceed from this result without a narrower scope or a corrected, independently reproducible corpus analysis.

The Phase 0A synthetic fixture remains green: 24/24 patterns, normalization 2/2, Options sections 5/5, and 4 dynamic accesses with no missing classification.

## Corpus and independent labels

The checked-in corpus index is [corpus-index.json](corpus-index.json). It contains only public repository URLs, exact commit SHAs, and selection rationale. No cloned source is committed. The ten selected repositories are:

| Repository | Pinned commit | Qualification |
| --- | --- | --- |
| [eShopOnWeb](https://github.com/dotnet-architecture/eShopOnWeb) | `4da8212117e87d808d4bbc7da6286fd2147ce606` | Public ASP.NET Core reference application with `IConfiguration` access and checked-in appsettings surfaces. |
| [CleanArchitecture](https://github.com/jasontaylordev/CleanArchitecture) | `11bf720f0ef536a19f2d734e73cb5c1600e61f77` | Public ASP.NET Core clean-architecture application with a Web project, configuration access, and appsettings surfaces. |
| [clean-architecture-manga](https://github.com/ivanpaulovich/clean-architecture-manga) | `68b1d5869dd9a7730c58eb0daf5051309eaf09a4` | Public ASP.NET Core clean-architecture sample with configuration access and appsettings surfaces. |
| [clean-minimal-api](https://github.com/Elfocrash/clean-minimal-api) | `6b73c8f69cacb44d32fec1ce2bd517d4f9013288` | Public ASP.NET Core minimal API sample with a web project and appsettings surfaces. |
| [dotnet-rpg](https://github.com/kernelcsh/dotnet-rpg) | `bb5db15c1e4c99ff4b141abb18b833d153f7c82a` | Public ASP.NET Core game API with IConfiguration/Options usage and appsettings surfaces. |
| [aspnet-core-mvc](https://github.com/webgentle/aspnet-core-mvc) | `e12a780e1a0202ecdb814d792a54d6d72d72621b` | Public ASP.NET Core MVC tutorial repository with configuration usage and appsettings surfaces. |
| [ASP.NET Core BackEnd Rest API](https://github.com/ggodin1981/ASP.NET_Core_BackEnd_Rest_API_JWT_Web_Token_Auth) | `7aa32d68b242c35216ed2b8f9dc3987cd6d49f81` | Public ASP.NET Core book catalog API with configuration usage and appsettings surfaces. |
| [dotnet-webapi-boilerplate](https://github.com/fullstackhero/dotnet-webapi-boilerplate) | `3f2959e683e9f83f13e55e1678c9119f63c7e8e5` | Public modular ASP.NET Core Web API with a selected host project, configuration usage, and appsettings surfaces. |
| [OidcProxy.Net](https://github.com/oidcproxydotnet/OidcProxy.Net) | `c6a695c05e197fe294c1585100e1c25a899bd7b0` | Public .NET OIDC/BFF repository with ASP.NET Core hosts, configuration access, and checked-in appsettings surfaces. |
| [razor-pages-IOptions-samples](https://github.com/karenpayneoregon/razor-pages-IOptions-samples) | `48ad402ee7c476afbf0f8daf19c7e021d339984b` | Public ASP.NET Core configuration sample collection with IConfiguration/Options usage and appsettings surfaces. |

The corresponding independently authored labels are `research/phase0b/labels/<repository-id>.json` for each index ID. The protocol is recorded in [LABELING.md](LABELING.md): labels were produced by manually reading source and JSON, not by running the analyzer; dynamic, unsupported, and uncertain accesses are retained as audit items; values and secrets are excluded. The selected `.csproj` is the source-analysis unit; project references may be loaded only to construct its compilation. Declaration truth is the repository-wide checked-in `appsettings*.json` graph.

Candidates excluded before labeling, with reasons, are also recorded in [LABELING.md](LABELING.md): no appsettings surface (`eShopModernizing`, `aspnetcore3-configuration`), unavailable pinned SDK (`clean-architecture-dotnet`, `ASP.NET-Core-Eduardo-Pires-WebApi`), or incomplete shallow checkout without verifiable appsettings (`dotnet-podcasts`). These exclusions were made before metric evaluation and were not changed to improve a threshold.

## Metrics

The single recomputation command, run from the repository root, was:

```powershell
$scratchRoot = Join-Path $env:TEMP 'configgap-phase0b-run'
pwsh -NoProfile -File .\scripts\Invoke-Phase0BCorpus.ps1 `
  -RepositoryRoot (Get-Location).Path `
  -ScratchRoot $scratchRoot `
  -OutputPath (Join-Path (Get-Location).Path 'research/phase0b/metrics.json') `
  -SkipRestore
```

The command exited `1`, as required for a failed gate. `-SkipRestore` is explicit because the restore-enabled corpus run stalled before reaching the evaluator on the first legacy repository; it must not be mistaken for a passing package/project compatibility result. The machine-readable output is [metrics.json](metrics.json).

Raw evaluator output:

```text
ConfigGap Phase 0B corpus evaluation
Repository                       TP/blocking  Predicted  Recall       Dynamic blocking
------------------------------  -----------  ---------  -----------  ----------------
aspnet-core-mvc                     0/0              0      8/8                     0
bookcatalog-api                     0/0              0      0/6                     0
clean-architecture-jason-taylor      1/1              1      1/1                     0
clean-architecture-manga            0/0              0      0/3                     0
clean-minimal-api                   0/0              0      0/1                     0
dotnet-rpg                          0/0              0      1/1                     0
eShopOnWeb                          3/3              3      4/4                     0
fullstackhero-webapi                0/0              0      0/3                     0
oidcproxy-net                       0/0              0      1/1                     0
razor-pages-ioptions-samples        0/0              0      1/1                     0

Blocking precision: 4/4 = 100.00%
Static-key recall: 16/29 = 55.17%
Dynamic blocking findings: 0
Machine-readable report: research/phase0b/metrics.json
Duration: 40872 ms
Restore skipped: True
Metric command duration: 41627 ms
Total corpus command duration: 81255 ms
Scratch clones present after cleanup: False
```

The per-repository numerators and denominators are in `metrics.json`. The precision gate passes (`4/4`), the recall gate fails (`16/29`), and the dynamic safety gate passes (`0`). The recall result must be treated as a narrow/insufficient feasibility result rather than tuned by deleting repositories or changing labels.

## Performance gate

No corpus repository was used as a 50-project representative solution. The probe deterministically generated one from the checked-in fixture corpus:

```powershell
$scratchRoot = Join-Path $env:TEMP 'configgap-phase0b-run'
$synthetic = Join-Path $scratchRoot 'synthetic-50'
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --generate-synthetic $synthetic --repository-root . --project-count 50
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --analyze-only --repository-root $synthetic `
  --solution (Join-Path $synthetic 'ConfigGap.Synthetic.sln') `
  --output (Join-Path $scratchRoot 'performance-1.json')
dotnet run --project .\tools\ConfigGap.Probe\ConfigGap.Probe.csproj -c Release --no-build -- `
  --analyze-only --repository-root $synthetic `
  --solution (Join-Path $synthetic 'ConfigGap.Synthetic.sln') `
  --output (Join-Path $scratchRoot 'performance-2.json')
```

The generator copied the Phase 0A C# and support sources, created 50 projects and one support project, and copied `appsettings.json`, `appsettings.Production.json`, and `.env.example` into each generated project. The two analysis runs were on the unchanged generated tree. Complete records are in [performance.json](performance.json).

| Run | Observations | Declared surfaces | Wall clock | Peak working set |
| --- | ---: | ---: | ---: | ---: |
| Primary | 1,151 | 150 | 21,700 ms | 207,577,088 bytes |
| Repeat | 1,151 | 150 | 22,718 ms | 202,653,696 bytes |

Mean wall clock was 22,209 ms; the absolute run difference was 1,018 ms (4.69% from the primary). The frozen v1 regression baseline is `<=22,718 ms` and `<=207,577,088 bytes` for this exact generated solution and machine, not a portable SLA.

Machine: Windows 10 Pro for Workstations 10.0.19045, x64; Lenovo 82L5; 16 logical processors; 14,877,257,728 bytes physical memory; .NET SDK 8.0.425; MSBuild 17.11.48; PowerShell 7.6.6.

## Framework/provider-owned inventory

The inventory is implemented by `FrameworkOwnedKeys` and is deliberately explicit. A key equal to an entry or below an entry's `:` hierarchy is excluded from application blocking findings. Application sections such as `Authentication`, `Identity`, `Serilog`, and custom provider sections remain application-owned. Raw `ASPNETCORE_` and `DOTNET_` spellings of the documented host keys are also reserved because the environment provider strips those prefixes when loading host configuration.

| Inventory | Primary evidence | v1 treatment |
| --- | --- | --- |
| `Kestrel:*`, `HTTP_PORTS`, `HTTPS_PORTS`, `urls`, `https_port` | [Kestrel configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/endpoints?view=aspnetcore-9.0), [Generic Host web settings](https://learn.microsoft.com/en-my/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-8.0) | Framework-owned; never an application drift error. |
| `Logging:*` | [ASP.NET Core logging configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/logging/?view=aspnetcore-8.0) | Framework/provider-owned; never an application drift error. |
| `AllowedHosts` | [Host filtering and proxy configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [HTTPS configuration example](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl?view=aspnetcore-10.0) | Framework middleware-owned; never an application drift error. |
| `ForwardedHeaders:*`, `FORWARDEDHEADERS_ENABLED` | [Proxy and load balancer configuration](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0) | Framework middleware-owned; never an application drift error. |
| `ConnectionStrings:*` | [ASP.NET Core configuration keys and connection-string provider rules](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-9.0) | Provider-owned root in v1; never an application drift error. Values are never read or emitted. |
| `applicationName`, `contentRoot`, `environment`, `webroot` and their raw prefixed environment names | [.NET Generic Host host settings](https://learn.microsoft.com/en-my/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-8.0) | Host-owned; never an application drift error. |
| `shutdownTimeoutSeconds`, `hostBuilder:reloadConfigOnChange`, startup/error/status/lifecycle settings (`captureStartupErrors`, `detailedErrors`, `hostingStartupAssemblies`, `hostingStartupExcludeAssemblies`, `preferHostingUrls`, `preventHostingStartup`, `startupAssembly`, `suppressStatusMessages`) and prefixed forms | [.NET Generic Host configuration settings](https://learn.microsoft.com/en-my/aspnet/core/fundamentals/host/generic-host?view=aspnetcore-8.0) | Host-owned; never an application drift error. |

This inventory covers the ordinary ASP.NET Core web-host and generic-host keys documented by Microsoft. It does not suppress arbitrary application configuration merely because a name sounds infrastructural.

## `.env.example` decision

`.env.example` is **not a first-class v1 declaration surface**. The independent corpus-label audit found `0/10` selected repository label files listing a `.env.example` file:

```powershell
$count = 0
Get-ChildItem .\research\phase0b\labels -Filter '*.json' | ForEach-Object {
  $label = Get-Content -Raw $_.FullName | ConvertFrom-Json
  if (@($label.reviewedFiles | Where-Object { $_.path -match '(^|/)\.env\.example$' }).Count -gt 0) { $count++ }
}
Write-Output "Label files listing .env.example: $count/10"
```

Output: `Label files listing .env.example: 0/10`. The synthetic benchmark includes `.env.example` only to preserve Phase 0A fixture coverage; that is not corpus prevalence evidence. Defer the surface in v1. If later demand justifies templates, use a generic explicitly declared template abstraction with a named path and parser, rather than baking one filename into the product contract. Actual `.env` files remain out of scope and are not read.

## Gate mapping

| Specification gate | Evidence | Result |
| --- | --- | --- |
| 7.3 precision >=95% | 4/4 = 100.00% | PASS |
| 7.3 recall >=90% | 16/29 = 55.17% | FAIL; recall is the named cause for narrowing |
| 7.3 dynamic access causes zero blocking findings | 0 | PASS |
| 7.3 fixture normalization and Options behavior | Phase 0A output: normalization 2/2, Options 5/5 | PASS |
| 7.4 practical ~50-project performance | 21,700 ms and 22,718 ms; peak 207,577,088 bytes | PASS as a measured seconds-scale baseline |
| 7.5 framework inventory | Explicit code list and Microsoft sources above | COMPLETE for ordinary ASP.NET Core host/provider keys |
| 7.5 `.env.example` research | 0/10 corpus label files | Decision recorded: defer first-class support |
| 7.6 park/narrow conditions | Recall below threshold; legacy restore/project compatibility remains unresolved | NARROW |

The result is deliberately not a PASS. The parent decision is required before any durable product CLI, packaging, telemetry, or release work.
