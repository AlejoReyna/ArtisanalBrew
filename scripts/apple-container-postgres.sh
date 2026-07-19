#!/usr/bin/env bash
set -euo pipefail

container_name="artisanalbrew-postgres-test"
host_port="55432"
connection="Host=127.0.0.1;Port=${host_port};Database=thiscafeteria_test;Username=test_only;Password=test_only_password"

case "${1:-start}" in
  start)
    if container inspect "${container_name}" >/dev/null 2>&1; then
      container start "${container_name}" >/dev/null || true
    else
      container run --detach --name "${container_name}" --publish "${host_port}:5432" \
        --env POSTGRES_DB=thiscafeteria_test --env POSTGRES_USER=test_only \
        --env POSTGRES_PASSWORD=test_only_password postgres:16
    fi
    for _ in $(seq 1 30); do
      if container exec "${container_name}" pg_isready -U test_only -d thiscafeteria_test >/dev/null 2>&1; then
        echo "TEST_POSTGRES_CONNECTION=${connection}"
        exit 0
      fi
      sleep 1
    done
    echo "PostgreSQL container did not become ready" >&2
    exit 1
    ;;
  stop) container stop "${container_name}" >/dev/null 2>&1 || true ;;
  remove) container stop "${container_name}" >/dev/null 2>&1 || true; container rm "${container_name}" >/dev/null 2>&1 || true ;;
  *) echo "Usage: $0 {start|stop|remove}" >&2; exit 2 ;;
esac
