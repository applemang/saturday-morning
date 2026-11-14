## Build, Test, and Development Commands

Every project must provide a `Makefile` exposing the following standard targets:

### Required Targets
- `make lint`
  Runs static analysis and formatting checks (e.g., `ruff check`, `ruff format --check`).

- `make typecheck`
  Runs type checking (e.g., `mypy`).

- `make test`
  Runs the test suite (e.g., `pytest`) with coverage enforcement.

- `make verify`
  Runs **lint + typecheck + tests** as a unified verification step.

### Hooks
If pre‑commit or post‑tool hooks are used, they must call the same commands above.

---

## Coding Style & Naming Conventions

These conventions apply to all code generated or modified by Claude:

### Language & Formatting
- Python 3.11+
- PEP 8 formatting
- Google‑style docstrings for public modules, classes, and functions
- Type hints required everywhere (explicit types for public APIs)

### Logging & Error Handling
- Use the standard `logging` module
- Log to stderr only
- No `print` statements in production code
- Fail fast internally; handle exceptions at clear call boundaries

### Naming
- `snake_case` for modules, variables, and functions
- `PascalCase` for classes
- Constants in `UPPER_SNAKE_CASE`

---

## Dependency & Licensing Requirements

All dependencies — direct or transitive — must use **OSI‑approved open‑source licenses**.

### Preferred (permissive)
- MIT
- BSD 2‑Clause / 3‑Clause
- Apache 2.0

### Allowed (only if necessary)
- GPL v2
- LGPL (any version)

### Prohibited
- GPL v3
- AGPL
- SSPL
- Any other restrictive or viral copyleft license not explicitly allowed

If a non‑preferred license is used, Claude must justify why no permissive alternative exists.

---

## Testing Guidelines

### General
- TDD (red/green/refactor) is mandatory
- Use `pytest`
- Enforce 100% coverage unless the project explicitly defines an exception
- Test files must live under `tests/`
- Test names must follow:
  - Files: `tests/test_*.py`
  - Functions: `test_*`

### Test Quality Expectations
- Tests must assert behavior, not implementation details
- Tests must be deterministic
- Tests must be isolated and not depend on external state
- Fixtures should be used for setup, not ad‑hoc inline scaffolding

---

## Commit & Pull Request Guidelines

### Commit Messages
Use **Conventional Commits**, for example:
- `feat: add user session manager`
- `fix: handle missing policy`
- `refactor: simplify token parsing`
- `test: add coverage for edge cases`

### Pull Requests
PRs should include:
- A clear summary of changes
- Linked issues (if applicable)
- Evidence that `make verify` passed
- Notes on any architectural or behavioral implications

### Review Expectations
- Code must materially satisfy the requirements
- Non‑blockers should be noted but do not prevent merging
- Blockers must be resolved before approval

---

## Claude‑Specific Expectations

When Claude is generating or modifying code:

- Follow TDD strictly
- Do not write implementation before tests
- Do not skip refactoring
- Do not introduce dependencies without checking license compatibility
- Do not modify unrelated code unless explicitly instructed
- Do not commit — only generate commit messages
- Always ensure the final output is consistent with this document

Jeltz is wired into this project at .jeltz (symlink to ~/.local/share/jeltz/). See .jeltz/CLAUDE.md.

---

## Project-specific rules (Saturday Morning)

These extend and, where stated, override the jeltz defaults above.

- **Two stacks.** C# Jellyfin plugin (`src/`, `tests/Jellyfin.Plugin.SaturdayMorning.Tests`)
  and Python release tooling (`builder/sm_release`, `tests/python`) plus the integration
  harness (`tests/integration`).
- **Everything user-facing is configured in the Jellyfin UI.** No config files or scripts
  for end users; new settings go on the plugin settings page, new work in scheduled tasks.
  `make verify` gates both: ruff, mypy (strict), `dotnet format --verify-no-changes`,
  Release build with warnings as errors (nullable + all .NET analyzers), pytest and
  xunit (Microsoft Testing Platform) with coverage thresholds.
- **Coverage exception (explicit).** 100% line+branch is enforced for all Python code and
  for the plugin's `Selection/`, `Configuration/` and `Commercials/` code. The Jellyfin adapter
  (`Adapter/`), scheduled task (`Tasks/`) and `Plugin.cs` need a running server and are
  excluded from unit coverage; they are covered by `make integration` against a real
  Jellyfin 12.1. Keep that layer thin: logic goes in `Selection/`.
- **Licence exception (explicit).** The project itself is GPL-3.0-or-later to match the
  Jellyfin plugin template. Jellyfin.Controller/Model (GPL-2.0) are compile-only host
  references (`ExcludeAssets=runtime`) and are not redistributed. Every other dependency
  must still follow the licence rules above. Test deps: xunit.v3 (Apache-2.0),
  coverlet.MTP (MIT); dev tools pinned in `requirements-dev.txt`.
- **Commits.** The jeltz "do not commit" rule is superseded for this repo: commit to
  `main` and push (owner instruction). Commit identity is repo-local
  `applemang <666288+applemang@users.noreply.github.com>` (public repo).
- **Jellyfin 12.** Target `net10.0`, `targetAbi 12.1.0.0`, modern
  `Authorization: MediaBrowser Token=` header only. Check signatures against
  `docs/jellyfin-12-api-notes.md` and the 12.1 source before changing adapter code.
- **No custom controllers / REST endpoints.** The admin page uses existing admin APIs only.
- **Public repo hygiene.** No hostnames, server paths, keys or infrastructure details of
  any deployment in this repo.
