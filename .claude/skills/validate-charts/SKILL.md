---
name: validate-charts
description: Validate VeloTimer Helm charts locally the way charts CI does (dependency update, lint, template, and the Python chart tests). Use after changing anything under charts/, src/MessageBus.Topology/topology.json, or chart-facing image or config values. Chart CI runs only for master, so v2 changes are not validated otherwise.
---

# Validate Helm charts

Four charts live in `charts/`: `velotime-library` (shared templates), `velotime-bootstrap`, `velotime-migration`, and `velotime` (application, including the Service Bus topology hook). `charts/tests/` holds Python `unittest` regression tests that render the charts with `helm template`.

## Steps

Run from the repository root. `helm` is installed in the devcontainer.

```bash
set -e
helm repo list 2>/dev/null | grep -q '^valkey' || helm repo add valkey https://valkey.io/valkey-helm/

for chart in charts/velotime/charts/*/ charts/velotime-migration/charts/*/; do
  helm dependency update "$chart"
done
for chart in velotime-library velotime-bootstrap velotime-migration velotime; do
  helm dependency update "charts/$chart"
done

for chart in velotime-library velotime-bootstrap velotime-migration velotime; do
  helm lint "charts/$chart"
done
for chart in velotime-bootstrap velotime-migration velotime; do
  helm template "$chart" "charts/$chart" > /dev/null
done

# Python tests need PyYAML; system pip is externally managed, so use a venv outside the repo
[ -x ~/.venvs/velotime-charts/bin/python ] || python3 -m venv ~/.venvs/velotime-charts
~/.venvs/velotime-charts/bin/pip install -q -r charts/tests/requirements.txt
~/.venvs/velotime-charts/bin/python -m unittest discover -s charts/tests -v
```

Run a single test module with `-p 'test_messagebus_topology.py'`.

## What the tests guard

- `test_messagebus_topology.py`: the hook Job ordering, secret handling, schema validation, and that the `messageBusTopology` defaults in `charts/velotime/values.yaml` equal `src/MessageBus.Topology/topology.json`.
- `test_migration_hooks.py`: the ordering and failure behavior of the schema-migration hooks.
- `test_*_messagebus.py`, `test_notifications_platform.py`: per-service wiring, images, and runtime dependencies.

## Also check

- `values.schema.json` when adding or renaming values.
- `charts/velotime/README.md` and the README [Helm schema migrations](../../../README.md#helm-schema-migrations) or [topology](../../../README.md#message-bus-topology-deployment) sections when operator-facing behavior changes.
- `actionlint` after workflow edits and `yamllint` for YAML outside templates.

## Do not

- Run `helm install`/`upgrade` against a cluster, or `helm package`/publish, without explicit approval. Publishing is done by [charts-publish.yml](../../../.github/workflows/charts-publish.yml).
- Commit `charts/**/charts/*.tgz` dependency archives unless the repository already tracks that file.
- Report a failure as caused by your change before checking it on the base commit. Because chart CI does not run on `v2`, failures that already exist there are possible.
