# Gate A and Gate B Checklist

## Gate A: measured active sites (end of Phase 0)
- Source: telemetry admin endpoint (private admin key, see telemetry repo README); count sites with a ping in
  the last 30 days, by plugin version and Umbraco major.
- Rule from the roadmap: fewer than 50 active sites means insert a growth sprint before building Premium at
  scale. Telemetry is opt-in, so treat the count as a floor, and note NuGet download trend next to it.
- Record: date, active sites, versions split, NuGet 30-day downloads.

## Gate B (confirmed by the owner 2026-10-12)
Go to Phase 1 build if at least 3 interviews give strong signals for the same capability AND at least 3 people
agree to try a free alpha. Otherwise rerun 5 more interviews with a narrower segment (agencies with 5+ sites).

## Outputs of Phase 0.4
- Filled tracker (10-15 rows), a one-page synthesis of top 3 pains, a ranked Premium feature list, and a
  yes/no on pricing assumptions (annual price range heard, who pays).
