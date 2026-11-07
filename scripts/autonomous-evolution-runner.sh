#!/usr/bin/env bash
set -euo pipefail

# ==============================================================================
# AutonomousAgentHub — Self-Evolution Runner Script ($0 Cost Architecture)
# Inspects delta since lastInspectedCommitSha, builds & verifies the .NET 8
# solution with Warnings-as-Errors, executes the xUnit test suite, and runs
# the orchestrator verification pipeline.
# ==============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
LEDGER_FILE="${REPO_ROOT}/docs/autonomous-evolution/state-ledger.json"
DRY_RUN="${DRY_RUN:-true}"
TARGET_REPO="${TARGET_REPOSITORY:-}"

cd "${REPO_ROOT}"

echo "================================================================================"
echo "[AutonomousEvolutionRunner] Repository Root : ${REPO_ROOT}"
echo "[AutonomousEvolutionRunner] State Ledger    : ${LEDGER_FILE}"
echo "[AutonomousEvolutionRunner] Dry-Run Mode    : ${DRY_RUN}"
echo "================================================================================"

CURRENT_HEAD_SHA="$(git rev-parse HEAD)"
echo "[AutonomousEvolutionRunner] Current HEAD SHA: ${CURRENT_HEAD_SHA}"

if [ -f "${LEDGER_FILE}" ]; then
  LAST_SHA="$(grep -oE '"lastInspectedCommitSha"\s*:\s*"[^"]*"' "${LEDGER_FILE}" | head -n 1 | sed -E 's/.*"([^"]*)"$/\1/' || true)"
  if [ -n "${LAST_SHA}" ] && git cat-file -e "${LAST_SHA}^{commit}" 2>/dev/null; then
    echo "[AutonomousEvolutionRunner] Delta-First Context Budget: computing diff ${LAST_SHA}..${CURRENT_HEAD_SHA}"
    git diff --stat "${LAST_SHA}..${CURRENT_HEAD_SHA}" || true
  else
    echo "[AutonomousEvolutionRunner] Previous SHA '${LAST_SHA:-none}' not found in local history; inspecting current HEAD."
  fi
fi

echo "[AutonomousEvolutionRunner] Step 1/4: Restoring NuGet packages..."
dotnet restore AutonomousAgentHub.sln

echo "[AutonomousEvolutionRunner] Step 2/4: Building solution in Release mode (TreatWarningsAsErrors=true)..."
dotnet build AutonomousAgentHub.sln --configuration Release --no-restore

echo "[AutonomousEvolutionRunner] Step 3/4: Executing xUnit test suite..."
dotnet test AutonomousAgentHub.sln --configuration Release --no-build --verbosity normal

echo "[AutonomousEvolutionRunner] Step 4/4: Running AutonomousAgentHub CLI verification..."
CLI_ARGS=()
if [ "${DRY_RUN}" = "true" ]; then
  CLI_ARGS+=("--dry-run")
fi
if [ -n "${TARGET_REPO}" ]; then
  CLI_ARGS+=("--repo" "${TARGET_REPO}")
fi

dotnet run --project src/AutonomousAgentHub.Core/AutonomousAgentHub.Core.csproj --configuration Release --no-build -- "${CLI_ARGS[@]}"

echo "[AutonomousEvolutionRunner] Cycle completed cleanly."

