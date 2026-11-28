#!/usr/bin/env bash
# Run `dotnet test` with the PostgreSQL environment CI provides.
#
#   test-with-postgres.sh [dotnet test args...]   start (or reuse) the test server, then run tests
#   test-with-postgres.sh --env                   start the server and print export lines only
#   test-with-postgres.sh --stop                  remove the test server container
#
# The container is disposable and test-only. Override the host port with
# VELOTIME_TEST_PG_PORT (default 55432, chosen to avoid an AppHost PostgreSQL on 5432).
set -euo pipefail

container=velotime-test-postgres
port="${VELOTIME_TEST_PG_PORT:-55432}"
password=outbox-tests
image=postgres:17-alpine

if [[ "${1:-}" == "--stop" ]]; then
  docker rm -f "$container" >/dev/null 2>&1 || true
  echo "Removed $container"
  exit 0
fi

if [[ -z "$(docker ps -q --filter "name=^${container}$")" ]]; then
  docker rm -f "$container" >/dev/null 2>&1 || true
  docker run -d --rm --name "$container" -p "127.0.0.1:${port}:5432" \
    -e POSTGRES_PASSWORD="$password" "$image" >/dev/null
fi

for _ in $(seq 1 60); do
  if docker exec "$container" pg_isready -U postgres >/dev/null 2>&1; then break; fi
  sleep 1
done
docker exec "$container" pg_isready -U postgres >/dev/null

connection="Host=localhost;Port=${port};Database=postgres;Username=postgres;Password=${password}"
vars=(
  VELOTIME_TEST_OUTBOX_POSTGRES
  VELOTIME_TEST_STATISTICS_POSTGRES
  FACILITIES_TEST_POSTGRES_ADMIN_CONNECTION
  NOTIFICATIONS_API_TEST_ADMIN_CONNECTION
  NOTIFICATIONS_WORKER_TEST_ADMIN_CONNECTION
)

if [[ "${1:-}" == "--env" ]]; then
  echo "export OIDC_AUDIENCE=https://build.invalid"
  for v in "${vars[@]}"; do echo "export $v='$connection'"; done
  exit 0
fi

export OIDC_AUDIENCE=https://build.invalid
for v in "${vars[@]}"; do export "$v=$connection"; done

exec dotnet test "$@"
