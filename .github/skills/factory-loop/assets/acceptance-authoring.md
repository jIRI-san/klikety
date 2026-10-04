# Factory-loop acceptance authoring

Use this guidance when `/cip` turns a factory-loop feature request into a confirmed plan. Keep the
acceptance contract optional and project-owned; it does not replace ordinary plan confirmation or
`/ci` admission.

For an acceptance scenario, name:

- the observable user-facing behavior and its expected result;
- the real command or test that checks it from the built artifact;
- the immutable artifact identity the result applies to;
- any required environment inputs, with secrets read only from the project's existing credential
  store;
- the safe failure result when the behavior, artifact identity, or access cannot be verified.

Prefer small deterministic tests over status flags. In the local demo, write an
`Invoke-Acceptance.ps1` that accepts `-ArtifactRoot` and `-Environment`, executes the built
application, and returns one schema-versioned JSON result with `status`, `scenario`, `expected`, and
`actual`. Run it against the immutable artifact snapshot, not the mutable working tree. Keep
telemetry acceptance separate from functional acceptance and require explicit operator approval
before production promotion.

Never put credentials, customer logs, or raw production data in an acceptance asset. Missing
permissions, query coverage, or artifact identity is blocked/inconclusive, not a passing result.
