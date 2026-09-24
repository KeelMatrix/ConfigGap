# Changelog

All notable changes to KeelMatrix.ConfigGap are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- A .NET 8 `configgap` tool package compares statically used configuration keys with declared repository surfaces.
- Deterministic text and JSON reports distinguish blocking known gaps from informational dynamic access.
- Local-only analysis supports `IConfiguration`, supported Options section binding, JSON declarations, and explicitly listed env-name templates.
- Literal reads use proven configuration-root provenance, including root types, known framework root properties, proven aliases, helper parameters with visible same-compilation call sites whose arguments are all proven roots, and bounded ASP.NET Core activation for conventional `Startup`, `ControllerBase`-derived, or explicitly framework-registered service constructors; outside those cases, parameters without a visible caller and other unproven receivers produce informational `CG900` rather than invented root reads.
- Best-effort activation and weekly heartbeat telemetry uses the shared contract and never receives keys, values, paths, or report contents.
