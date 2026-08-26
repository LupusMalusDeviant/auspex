# codebase-mapper

Runs of the `codebase-mapper` skill in this repository. Newest first.

## 2026-08-25 03:40 — Run

**Task:** Document the whole codebase as a blueprint, as part of the 0.9.0
renaming and documentation pass.

**Decisions:**
- Re-run mode: not asked — no `docs/blueprints/` existed, so this is the first
  run.
- Scope: not asked — the instruction was "document everything", so the whole
  repository.
- Feature list: not confirmed with the user, who was asleep and had asked for
  the work to continue. The 16 features were cut along the lines the codebase
  already draws (Go packages, `Services/` folders, the two optional
  components).
- Output language: **English**, deviating from the skill's German default. The
  same session had established that the whole codebase and all its
  documentation are English from 0.9.0 on.
- No `Explore` subagent: the session had already read the repository in full
  during the renaming, and this session is configured not to call the Agent
  tool unasked.

**Artefacts:**
- `docs/blueprints/INDEX.md`
- `docs/blueprints/resolver-pipeline.md`
- `docs/blueprints/rules-and-lists.md`
- `docs/blueprints/cache-and-upstreams.md`
- `docs/blueprints/learning-mode.md`
- `docs/blueprints/device-identity.md`
- `docs/blueprints/control-api.md`
- `docs/blueprints/ingest-and-storage.md`
- `docs/blueprints/detectors.md`
- `docs/blueprints/impact-analysis.md`
- `docs/blueprints/router-connection.md`
- `docs/blueprints/destinations-and-dossier.md`
- `docs/blueprints/browser-extension.md`
- `docs/blueprints/windows-sensor.md`
- `docs/blueprints/dashboard-ui.md`
- `docs/blueprints/localization.md`
- `docs/blueprints/operations.md`

**Status:** abgeschlossen

---
