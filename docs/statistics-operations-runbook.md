# Statistics operations runbook

This runbook covers the replay-safe Statistics pipeline (see [replay-safe-statistics-plan.md](replay-safe-statistics-plan.md), Phase 5). It explains the signals to watch, how to recompute a range, and what to do when each signal fires.

## How the pipeline can stall

```
Agent ─PassingObservedV1─▶ Timing.Processor ─timing.smb_outbox─▶ velotime-timing ─▶ Statistics.Processor
                                                                                    │ passing_fact + recompute_request
Facilities.Api ─facility.smb_outbox─▶ velotime-facilities ─────────────────────────▶│
Statistics.Api (definition edits, admin recompute jobs) ───────────────────────────▶│
                                                                                    ▼
                                                     recompute worker: samples, entries, unresolved passings
```

Work can pile up in four places:

1. **A producer outbox.** Timing and Facilities write events to their PostgreSQL outbox in the same transaction as the change. The outbox delivers them in order, one at a time. While the oldest message keeps failing, nothing behind it is sent.
2. **A dead-letter queue.** Service Bus moves a message to the subscription's dead-letter queue (DLQ) after `maxDeliveryCount` failed deliveries (Service Bus default: 10). A dead-lettered event is not processed until someone resubmits it.
3. **The recompute queue** (`statistics.recompute_request`). Every result change goes through it: live and late passings, configuration changes, definition edits and admin recompute jobs. A failed recompute is retried with exponential backoff (5 s doubling to 5 min, from `Statistics:Recompute:FailureDelay` and `MaxFailureDelay`).
4. **Unresolved passings** (`statistics.unresolved_passing`). A passing that has no facility or course point at its time stays a fact but produces no results. This is not an error, but it usually means configuration is missing.

## Signals

All gauges are sampled from the database by a background monitor, so every replica reports the same value. Aggregate them with `max`, not `sum`. Gauges report nothing until the first sample after start-up.

| Instrument (meter) | Meaning | Suggested alert |
|---|---|---|
| `velotime.module.statistics.recompute.backlog` `{kind=transponder\|range}` (VeloTime.Module.Statistics) | Pending recompute requests (dirty backlog). Range requests are split into one transponder request each before they run. | Informational. Large after a bulk recompute, which is expected. |
| `velotime.module.statistics.recompute.oldest_request_age` (s) | Age of the oldest pending request (oldest dirty age). | Warn above 15 min, page above 1 h. A bulk recompute raises it legitimately; check for an active job first. |
| `velotime.module.statistics.recompute.failing_requests` | Requests retrying after an error. | Warn when above 0 for 10 min. |
| `velotime.module.statistics.recompute.failures` (counter) | Failed recompute attempts. | Rate above 0 for 10 min. |
| `velotime.module.statistics.unresolved_passings.backlog` | Passing facts without a facility or course point at their time. | Warn when it grows for 1 h. |
| `velotime.module.statistics.manual_decision_reviews.backlog` and `.oldest_age` (s) | Manual invalidations a recompute could not keep (plan §5.4). | Warn when the oldest is more than 7 days old. |
| `velotime.messaging.outbox.oldest_age` `{outbox=timing\|facility}` (s) (VeloTime.Messaging) | Age of the oldest undelivered outbox message. | Warn above 5 min, page above 30 min. |
| `velotime.messaging.outbox.pending` and `.aborted` `{outbox}` | Undelivered and aborted outbox messages. | Any aborted message. |
| `velotime.messaging.dead_letter.messages` `{topic, subscription}` | Messages in a subscription's DLQ, capped at the peek limit (default 1000). | Any value above 0. |
| `velotime.messaging.dead_letter.new` (counter) `{topic, subscription}` | Dead letters first seen since the host started. | Rate above 0. |

Every newly dead-lettered message is also logged once per process at **Error** level by `DeadLetterMonitor`, with its topic, subscription, message name, message ID, session, delivery count, and dead-letter reason and description. A log-based alert on that logger is the simplest DLQ alert. After a restart, messages that are still in the DLQ are logged again once.

Which host reports what:

| Host | Reports |
|---|---|
| Statistics.Processor | Statistics backlog gauges; DLQs `velotime-timing/statistics` and `velotime-facilities/statistics` |
| Timing.Processor | Outbox `timing` (shared with Timing.Api); DLQs `velotime-agent/timing` and `velotime-agent/infrastructure` |
| Facilities.Api | Outbox `facility` |

With an OTLP-to-Prometheus pipeline, dots become underscores and the unit is appended, for example `velotime_module_statistics_recompute_oldest_request_age_seconds`. An example rule:

```yaml
- alert: StatisticsRecomputeStalled
  expr: max(velotime_module_statistics_recompute_oldest_request_age_seconds) > 3600
  for: 10m
- alert: ServiceBusDeadLetters
  expr: max by (topic, subscription) (velotime_messaging_dead_letter_messages) > 0
  for: 5m
```

## Recompute a range

Recompute after you fix configuration, deploy a derivation fix, or repair data. Recomputing unchanged facts and configuration changes nothing, so a recompute is always safe. It only costs time.

**Admin UI:** go to *Statistics Administration → Recompute statistics* (`/admin/statistics/recompute`). Choose the range, and optionally one transponder or the transponders seen at one installation, then queue it. With no bounds and no scope you must confirm that you want to recompute all history. The page shows the backlog and the progress of the latest jobs, and it refreshes every 5 seconds while work is pending.

**API** (needs the Statistics administrator role):

```http
POST /api/statistics/admin/recompute/jobs
{ "fromUtc": "2026-11-20T00:00:00Z", "toUtc": "2026-11-21T00:00:00Z", "installationId": null, "transponderId": null, "note": "layout fix" }

GET /api/statistics/admin/recompute            # backlog and latest 20 jobs
GET /api/statistics/admin/recompute/jobs/{id}  # one job
```

What happens next:

- The job inserts one `Manual` recompute request, and the processor picks it up within `Statistics:Recompute:IdlePollInterval` (30 s by default). The API cannot wake the worker.
- The worker splits a range request into one request per transponder that has facts in the range. The job's total is set then, and the job shows as *Running*.
- Each transponder's range is widened to whole riding sessions (passings less than `SessionGap`, 30 min, apart), so results just outside the range can change too.
- Live passings are always claimed before bulk work, so a large recompute does not delay live results.
- A job is *Completed* when none of its requests remain. *Retrying* counts transponders whose recompute failed and is waiting for its backoff, and the last error is shown.

## Procedures

### Recompute backlog is old or growing

1. Open the recompute page. If a large job is running and its remaining count keeps falling, the backlog is expected. Wait for it to finish.
2. If requests are *retrying*, read the last error. The usual cause is that Facilities or Timing is unreachable. Configuration history is read through the shared cache, and on purpose there is no stale fallback (plan §4.3), so a recompute fails while the owner of the history is down. Fix the dependency. The requests then retry on their own.
3. To retry now instead of waiting for the backoff:

   ```sql
   UPDATE statistics.recompute_request SET not_before_utc = NULL WHERE attempts > 0;
   ```

4. If nothing is retrying and nothing progresses, check that Statistics.Processor is running and look for `Statistics recompute step failed before claiming work` in its logs. That message usually means the database is unavailable.

Inspect the queue:

```sql
SELECT reason, transponder_id IS NULL AS range_request, count(*), min(created_at_utc), max(attempts)
FROM statistics.recompute_request GROUP BY 1, 2;

SELECT transponder_id, attempts, not_before_utc, last_error
FROM statistics.recompute_request WHERE attempts > 0 ORDER BY not_before_utc DESC LIMIT 20;
```

### Unresolved passings

A passing is unresolved when its installation had no facility assignment at its time (`NoFacility`), or its timing point had no course point association (`NoCoursePoint`).

```sql
SELECT installation_id, timing_point_id, facility_id, reason, count(*), min(time), max(time)
FROM statistics.unresolved_passing GROUP BY 1, 2, 3, 4 ORDER BY 5 DESC;
```

To fix it, add the missing installation-facility assignment (Timing) or timing-point association (Facilities), backdated to cover the passings. The change event recomputes the affected range, and the rows disappear. If they remain, queue a recompute for that installation and range.

The table is filled as each transponder is recomputed. **After deploying the release that adds it, queue one recompute of all history** so passings recorded earlier are included.

### Manual decision reviews

When a recompute changes or removes an entry that an administrator invalidated, the decision is kept in `statistics.manual_decision_review`. There is no admin page for these yet ([#144](https://github.com/asbjorjo/VeloTimer/issues/144)). Until there is, review them in the database:

```sql
SELECT created_at_utc, outcome, transponder_id, statistics_item_id, time_start, time_end, duration, note, changed_by_user_id
FROM statistics.manual_decision_review ORDER BY created_at_utc;
```

Re-apply the invalidation to the replacing entry on *Statistics entries* if it still applies, then delete the review row.

### Outbox is not draining

1. Check the host's logs (Timing.Processor or Timing.Api for `timing`, Facilities.Api for `facility`) for Service Bus send errors. Common causes are a wrong or expired connection string, a missing topic (the topology job did not run), or a namespace outage.
2. The outboxes retry without limit (`MaxDeliveryAttempts = int.MaxValue`), so they recover once the bus is reachable. They never skip a message.
3. Inspect:

   ```sql
   SELECT message_type, path, delivery_attempt, timestamp FROM timing.smb_outbox
   WHERE NOT delivery_complete AND NOT delivery_aborted ORDER BY timestamp LIMIT 20;
   ```

### Messages are dead-lettered

1. Find the Error log entry from `DeadLetterMonitor`. It names the message and gives the dead-letter reason. `MaxDeliveryCountExceeded` means the handler failed on every delivery. Look up the handler's errors for that message ID in the consumer's logs.
2. Fix the cause. Typical causes: an invalid message (the handler rejects it), cache eviction failing (configuration change handlers retry rather than enqueue on a stale cache, plan §4.3), or the database being unavailable for longer than the redeliveries took.
3. Resubmit the message from the DLQ (for example with Service Bus Explorer in the Azure portal). All Statistics handlers are idempotent, so resubmitting a message that was partly processed is safe. A resubmitted configuration change queues its own recompute.
4. If a message cannot be fixed, delete it from the DLQ. Then queue a recompute covering its time range, so the results reflect the facts and configuration Statistics has.

## Configuration

| Setting | Default | Host |
|---|---|---|
| `Statistics:Recompute:SessionGap` | 00:30:00 | Statistics.Processor |
| `Statistics:Recompute:IdlePollInterval` | 00:00:30 | Statistics.Processor |
| `Statistics:Recompute:FailureDelay` / `MaxFailureDelay` | 00:00:05 / 00:05:00 | Statistics.Processor |
| `Statistics:Operations:SampleInterval` | 00:00:30 | Statistics.Processor |
| `Messaging:Monitoring:Enabled` | true | hosts with an outbox or DLQ monitor |
| `Messaging:Monitoring:OutboxSampleInterval` | 00:00:30 | Timing.Processor, Facilities.Api |
| `Messaging:Monitoring:DeadLetterSampleInterval` | 00:01:00 | Timing.Processor, Statistics.Processor |
| `Messaging:Monitoring:DeadLetterPeekLimit` | 1000 | Timing.Processor, Statistics.Processor |

The dead-letter monitor peeks with the host's runtime `ConnectionStrings:MessageBus`. Peeking needs only Listen rights, which the consumers already have. A host without that connection string, such as a local run without Service Bus, skips the monitor.
