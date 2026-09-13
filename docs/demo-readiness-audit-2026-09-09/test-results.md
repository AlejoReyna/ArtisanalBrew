# Test results — Part 1 (2026-09-09)

Environment: Cursor box; Release build; isolated local PostgreSQL 17 (`thiscafeteria_test` / `test_only` on 127.0.0.1:5432). Not Mac; not production DB.

## .NET
| Suite | Command | Exit | Duration | Pass | Fail | Skip |
|---|---|---|---|---|---|---|
| Restore | `dotnet restore ThisCafeteria.sln` | 0 | ~7s | — | — | — |
| Build Release | `dotnet build --configuration Release --no-restore` | 0 | ~16s | — | 0 errors | warnings (NU1608/NU1510/NU1903, CS0162/CS8604/CS0649) |
| Unit | `dotnet test tests/ThisCafeteria.UnitTests ...` | 0 | ~5s | 373 | 0 | 0 |
| Architecture | `dotnet test tests/ThisCafeteria.ArchitectureTests ...` | 0 | ~1s | 11 | 0 | 0 |
| Integration | `dotnet test tests/ThisCafeteria.IntegrationTests ...` + `TEST_POSTGRES_CONNECTION` | 0 | ~11s | 17 | 0 | 0 |

TRX artifacts: `test-results/unit.trx`, `arch.trx`, `integration.trx`

## AgentGateway
| Step | Exit | Notes |
|---|---|---|
| `npm ci` | 0 | ~4s; 3 npm audit vulns reported |
| `npm test` | 0 | tests 21; pass 20; skip 1 (Postgres store persistence); fail 0 |
| `npm run build` (`tsc`) | 0 | |

## contracts/evm
| Step | Exit | Notes |
|---|---|---|
| `npm ci` | 0 | |
| `npm test` (Node 20) | 1 | Hardhat requires Node ≥ 22.13 |
| `npm test` (Node 22.14) | 0 | ~25s; **29 passing** |

## contracts/solana
| Step | Exit | Notes |
|---|---|---|
| `cargo test --locked` | 0 | ~25s; **4 passed** (warnings) |
| `npm ci && npm run test:browser` | 0 | ~4s; **3 passing** |

## Skipped (with reason)
| Item | Why |
|---|---|
| Mac `git status` / Mac-local SDK | Subagent cannot reach user Mac via machineId |
| `run-acceptance.sh` | Inspected: can `RESET_DB`, starts Hardhat + Worker background; requires ACCEPTANCE_ISOLATED + PG :5433 |
| CI `crossstack-verification` | Live Hardhat nodes, Rundler download, standing Worker — out of Part 1 isolation scope |
| `dotnet format --verify-no-changes` | Not required for journey baseline; deferred |
| `node tools/verify_pixel_crew.mjs` | Homepage policy; not core to five journeys; deferred |
| Docker compose stack | Docker absent on box; would also risk shared volumes if run carelessly on Mac |

## scratch/e2e
- `scratch/e2e/package.json` scripts.test is placeholder: `echo "Error: no test specified" && exit 1`
- Real browser-ish checks elsewhere: `scratch/*-check.js`, `tools/verify_pixel_crew.mjs`, Solana `test:browser`, pixel-home runbook manual browser acceptance
