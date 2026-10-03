# Readiness Gates

Filament is considered ready to hand to another developer when these gates pass:

- Source checkout: `dotnet run --project tools/Filament.Verify/Filament.Verify.csproj`
  succeeds from the repository root.
- Demo/onboarding: `scripts/demo-smoke.sh` or `scripts/demo-smoke.ps1` prints
  loaded modules and `describe(...)` output without manual intervention.
- NuGet/package distribution: `scripts/package-smoke.sh` or
  `scripts/package-smoke.ps1` proves package contents and fresh
  `RadicalBeard.Filament.Core`/`RadicalBeard.Filament.Godot` consumers.
- Agent setup: `scripts/agent-smoke.sh` or `scripts/agent-smoke.ps1` validates
  JSON config, Python syntax, and survey rendering.
- Cross-platform: GitHub Actions runs `scripts/verify.ps1` on Ubuntu, macOS, and
  Windows.
- Documentation: `README.md`, `docs/distribution.md`, `docs/package-usage.md`,
  `docs/scripting.md`, `docs/godot.md`, and `docs/agent-setup.md` are present.

Known non-blocking release choice:

- The package license file is conservative and all-rights-reserved. Replace it
  before public open-source distribution if a permissive license is desired.
