.PHONY: lint typecheck test verify package integration demo clean

PY := $(if $(wildcard .venv/bin/python),$(CURDIR)/.venv/bin/python,python3)
DOTNET := $(if $(wildcard $(HOME)/.dotnet/dotnet),$(HOME)/.dotnet/dotnet,dotnet)
export DOTNET_CLI_TELEMETRY_OPTOUT := 1
export DOTNET_NOLOGO := 1

# Unit coverage covers the pure selection and configuration code. The Jellyfin
# adapter, scheduled task and plugin entry point need a running server and are
# covered by `make integration` (documented exception, see CLAUDE.md).
COVERLET := --coverlet --coverlet-output-format cobertura \
	--coverlet-include "[Jellyfin.Plugin.SaturdayMorning]*" \
	--coverlet-exclude-by-file "**/Adapter/*.cs" \
	--coverlet-exclude-by-file "**/Tasks/*.cs" \
	--coverlet-exclude-by-file "**/Plugin.cs" \
	--coverlet-exclude-by-file "**/*.g.cs" \
	--coverlet-threshold 100 --coverlet-threshold-type line,branch

lint:
	$(PY) -m ruff check .
	$(PY) -m ruff format --check .
	$(DOTNET) format SaturdayMorning.sln --verify-no-changes

typecheck:
	$(PY) -m mypy
	$(DOTNET) build SaturdayMorning.sln -c Release -warnaserror

test:
	$(PY) -m pytest --cov-fail-under=100
	$(DOTNET) test --project tests/Jellyfin.Plugin.SaturdayMorning.Tests -c Release -- $(COVERLET)

verify: lint typecheck test

package:
	scripts/package.sh

integration:
	$(PY) -m pytest tests/integration --no-cov -v

clean:
	rm -rf artifacts TestResults it-runs
	$(DOTNET) clean SaturdayMorning.sln >/dev/null

# Manual Jellyfin Web check: SM_IT_PASSWORD=<choose one> make demo
demo:
	@test -n "$$SM_IT_PASSWORD" || { echo "set SM_IT_PASSWORD first" >&2; exit 2; }
	SM_IT_HOLD=1 $(PY) -m pytest tests/integration --no-cov -s -q
