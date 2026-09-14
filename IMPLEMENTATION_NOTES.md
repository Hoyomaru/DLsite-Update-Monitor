# Implementation notes — milestone 1

## Frozen contract

`SnapshotComparer.Compare(acknowledged, candidate)` is intentionally side-effect free.

It never accepts the previous observation as the comparison baseline. The orchestrator must always supply the user's last acknowledged snapshot.

### Safety rule

If any monitored field regresses from known/parsed data into missing or unparseable data, the result is `Indeterminate`, not `Changed` and not `NoChange`.

The orchestrator must not replace `AcknowledgedSnapshot` or `CurrentSnapshot` when comparison/validation is indeterminate.

### Monitoring vs check health

`MonitoringState` and `CheckHealth` are independent. A network error must not erase a pending update detected by an earlier successful check.

## Product identity

The resolver only accepts exact `dlsite.com` or a subdomain ending in `.dlsite.com` and extracts an ID only from `/product_id/<ID>`.

Supported v1 IDs follow the same current shape used by DLsite metadata providers: two letters plus 6 or 8 digits. This can be loosened later in one place if DLsite introduces a new ID shape.

## State mutation guard

`ComparisonResult.TargetState` is nullable. `Indeterminate` and `IdentityMismatch` return `null`; callers must interpret null as **do not mutate MonitoringState**. This makes preservation of an existing pending state the safe default.

## Parser contract

The parser intentionally treats a missing update-information row as a valid `Missing` observation, but a present-yet-empty/unparseable row as `Unparsed`. File size is mandatory for comparison in v1; missing or unparseable file size yields a degraded snapshot and the orchestrator must preserve all acknowledged/current state.

Normalization is deterministic: Unicode NFKC, whitespace normalization, embedded 20xx dates normalized to `yyyy-MM-dd`, and file sizes converted to bytes using a 1024-based unit scale. Raw source text is preserved alongside normalized values.

## Tracking state machine

Network/check health is updated independently from monitoring state. `RecordCheckFailure` is forbidden from mutating acknowledged/current snapshots or `MonitoringState`. Successful but indeterminate comparisons store only `LastObservation` and diagnostic health; they do not advance the current or acknowledged state.

Acknowledging a pending change clones `CurrentSnapshot` into `AcknowledgedSnapshot`. Applied and Ignored use the same snapshot transition but distinct history event types.

## Persistence

`TrackingRepository` writes `tracking.tmp`, immediately deserializes and validates it, then replaces the primary file while retaining `tracking.backup.json`. Unsupported newer schema versions throw and are never silently overwritten.

## HTTP client contract

`DlsiteHttpClient` serializes all fetches with a semaphore, enforces a minimum interval between request starts, uses a per-attempt timeout, classifies HTTP failures, and retries only rate limiting, timeout, transport errors, and 5xx responses. HTTP 403 and 404/410 are terminal. `Retry-After` is preferred over configured retry delays.

The production factory supplies `locale=ja_JP` and `loginchecked=1` cookies; tests inject `HttpClient` and fake delay/clock implementations so no live DLsite requests are required.

## UpdateCheckService contract

The service verifies requested-vs-resolved product identity before allowing a first baseline to be created. This closes a critical edge case where an old DLsite URL redirects to another product and there is no acknowledged snapshot yet for `SnapshotComparer` to compare against.

Only healthy, safely comparable snapshots enter the 24-hour cache. Cache hits are cloned and do not mutate the stored entry. A cached observation keeps its original `FetchedAtUtc`; `LastAttemptAtUtc` may advance, but `LastSuccessfulCheckAtUtc` reflects when DLsite was actually observed.

## Static SDK compatibility review

The adapter is pinned to PlayniteSDK 6.16.0 for the current Playnite 10.56 stable line. Current Playnite source confirms the APIs used by the adapter (`GameMenuItemActionArgs.Games`, `ActivateGlobalProgress`, `GlobalProgressResult.Error`, `BufferedUpdate`, `GetPluginUserDataPath`, application lifecycle events) remain available.

Playnite 10.56 itself references Newtonsoft.Json 10.0.3, matching Core's compile-time reference. The net462 build excludes Newtonsoft and AngleSharp runtime assets so the extension does not ship competing copies of Playnite's own dependencies.

## Progress error contract

The Playnite adapter inspects `GlobalProgressResult.Error`. If a checkpoint/final persistence write throws, tag projection is stopped and the user receives an error instead of being shown a misleading success summary.

State-mutating context-menu operations (Applied / Ignored / Reset) share the same operation semaphore as the batch checker so they cannot race the tracking database while a check is running.

## Orchestration regression tests

`UpdateCheckServiceTests` cover baseline creation, redirect-to-different-product before baseline, preservation of pending state across HTTP failures, preservation of current state across degraded parsing, and cache reuse without a second HTTP request.

## Milestone 3.2

- Added explicit `System.Net.Http` framework reference to `DLsiteUpdateMonitor.Core` for the `net462` target.
- Reason: `HttpClient` is used by Core, so referencing it only from the Playnite plugin project is insufficient when Core is compiled independently for .NET Framework 4.6.2.
- Added a static validation rule to prevent this regression.
