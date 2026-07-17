# Finance Anomaly Detector

[![CI](https://github.com/Alexandru2984/f_sharp/actions/workflows/ci.yml/badge.svg)](https://github.com/Alexandru2984/f_sharp/actions/workflows/ci.yml)

A multi-tenant personal finance API and dashboard written in **F# (.NET 8)** with
**Giraffe**, **Dapper** and **SQLite**. It tracks expenses and budgets, and runs a
rule-based anomaly engine that flags suspicious spending: statistical outliers,
duplicate charges, subscription price hikes, merchant category switches and
late-night transactions. The frontend is a dependency-free vanilla JS SPA
(Chart.js vendored locally) served by the same process.

## Features

- **Anomaly engine v2** — six detection rules with stable rule codes:
  | Rule | What it flags |
  |------|---------------|
  | `CAT_OUTLIER` | z-score ≥ 3 vs. the category history (3x-average fallback on small samples) |
  | `MERCHANT_SPIKE` | amount over 4x the merchant's average |
  | `CAT_CHANGE` | merchant showing up in an unusual category |
  | `DUPLICATE` | same merchant + amount within 10 minutes |
  | `SUB_HIKE` | a stable recurring charge suddenly 20%+ more expensive |
  | `NIGHT` | transactions between 1 AM and 4 AM |

  Dismissed anomalies are remembered per `(expense, rule)` pair and never
  resurrected by later runs.
- **Recurring charge detection** — subscriptions/rent/utilities identified by
  amount stability across 3+ distinct months.
- **Monthly reports** — per-category shares, top merchants, month-over-month
  change, anomaly counts.
- **Budgets** — per-category monthly limits with live progress.
- **Expense management** — paginated + filtered listing (search, category,
  date range), inline editing, CSV import (10k rows / 5 MB cap, transactional)
  and RFC 4180 CSV export.
- **Multi-tenant auth** — cookie sessions, BCrypt password hashing, per-IP
  rate limiting on auth endpoints, strict CSP, per-user data isolation
  enforced in every query.

## Architecture

```
src/FinanceAnomalyDetector/
  Domain.fs        # records shared across layers
  Validation.fs    # pure input validation
  Recurring.fs     # pure recurring-charge detection
  Storage.fs       # SQLite via Dapper (WAL, FKs, indexes, migrations)
  AnomalyRules.fs  # pure detection rules
  AnomalyEngine.fs # orchestration + resolution memory
  CsvImport.fs     # pure CSV parsing
  Stats.fs         # pure aggregations + storage-backed wrappers
  Program.fs       # Giraffe routes, middleware, config
tests/             # xUnit suite (58 tests)
public/            # vanilla JS SPA
```

The core follows *functional core, imperative shell*: rules, stats, CSV
parsing and recurring detection are pure functions over in-memory data —
that's what makes the test suite fast and DB-free for most of its surface.

Data access is deliberately synchronous: SQLite executes on the calling
thread either way, so async wrappers would only add overhead here. Swapping
SQLite for a networked DB would be the moment to revisit that.

## Running

```bash
dotnet run --project src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj
# listens on http://localhost:5000
```

### Tests

```bash
dotnet test
```

### Docker

```bash
docker build -t finance-anomaly-detector .
docker run -p 8080:8080 -v finance-data:/data finance-anomaly-detector
```

## Configuration

All settings come from environment variables (a `.env` file is honored, but
real environment variables win). See `.env.example`.

| Variable | Default | Purpose |
|----------|---------|---------|
| `APP_PORT` | `5000` | Kestrel port (localhost binding) |
| `ASPNETCORE_URLS` | – | overrides `APP_PORT`, e.g. in containers |
| `DB_PATH` | `data/finance.db` | SQLite database file |
| `PUBLIC_DIR` | `./public` | static frontend directory |
| `ADMIN_USERNAME` / `ADMIN_PASSWORD` | – | seed an admin account at startup (both required; nothing is seeded otherwise) |

The schema is created and migrated automatically at startup (WAL mode,
enforced foreign keys, covering indexes).

## API

Full endpoint documentation lives at [`/docs`](public/docs.html) on the
running app. Summary:

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/register`, `/api/login`, `/api/logout` | cookie auth (rate limited) |
| GET | `/api/me` | current user |
| GET | `/api/expenses` | paginated list; `page`, `pageSize`, `category`, `search`, `from`, `to` |
| POST | `/api/expenses` | create expense |
| PUT / DELETE | `/api/expenses/{id}` | update / delete (cascades anomalies) |
| POST | `/api/expenses/import-csv` | bulk import |
| GET | `/api/expenses/export` | CSV download |
| GET | `/api/anomalies` | open anomalies |
| POST | `/api/anomalies/run` | run the detection engine |
| PATCH | `/api/anomalies/{id}/resolve` | dismiss an anomaly |
| GET | `/api/recurring` | detected recurring charges |
| GET | `/api/stats`, `/api/categories`, `/api/trends` | dashboard aggregates |
| GET | `/api/reports/monthly?month=yyyy-MM` | monthly report |
| GET / POST | `/api/budgets` | list / upsert budgets |
| DELETE | `/api/budgets/{category}` | remove a budget |
| POST | `/api/account/change-password` | change password |
| GET | `/health` | liveness probe |

## Production deployment

The reference deployment runs the published app under systemd behind nginx
(`finance_nginx.conf`), with `EnvironmentFile` pointing at the `.env`:

```bash
dotnet publish src/FinanceAnomalyDetector/FinanceAnomalyDetector.fsproj -c Release -o publish
sudo systemctl restart finance-anomaly-detector
```

Security notes:
- passwords hashed with BCrypt; sessions via HttpOnly/Secure/SameSite=Strict cookies
- per-IP fixed-window rate limiting on `/api/login` and `/api/register`
- strict `Content-Security-Policy` (all assets self-hosted), forwarded-header
  handling behind the reverse proxy
- no default credentials are shipped
