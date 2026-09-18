# Security Policy

## Reporting a vulnerability

Report suspected vulnerabilities privately before public disclosure:

1. Email **keelmatrix@gmail.com**.
2. Open a private GitHub Security Advisory for this repository.

Do not create a public issue or include secrets, credentials, personal data, or unredacted customer configuration in a report. Include the affected package version, command, target framework, operating system, sanitized reproduction steps, and security impact when safe.

We aim to acknowledge reports within five business days and will provide follow-up as assessment proceeds. Routine questions and non-sensitive bug reports belong in the project's normal public channels.

## Supported versions

Security fixes are prioritized for the latest maintained `0.1.x` release line on supported .NET 8 environments. Older versions and unsupported runtimes may receive fixes case by case.

## Scope

This policy covers the ConfigGap tool, its reports, declaration parsing, workspace loading, package contents, and telemetry boundary. Vulnerabilities in analyzed applications, their dependencies, or a user's build environment should be reported to the relevant maintainer unless they expose a ConfigGap trust boundary.
