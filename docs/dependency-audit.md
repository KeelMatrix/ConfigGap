# Dependency vulnerability audit

The repository gate is [`scripts/Invoke-VulnerabilityAudit.ps1`](../scripts/Invoke-VulnerabilityAudit.ps1). It queries direct and transitive packages for known vulnerabilities using the repository-controlled `NuGet.config` source list.

The gate fails closed when restore/audit execution fails, output is not valid JSON, or any vulnerability record is present. There is no vulnerability allowlist or intentional exception in this repository. A future exception would require a narrowly scoped, repository-controlled record of the affected package/version, reason, mitigation, owner, and expiry before the gate could be changed.
