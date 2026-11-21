# Application Insights Queries

KQL queries for checking the API's logs and request data in Application Insights. Set-up:
[AzureDeployment.md](AzureDeployment.md#3-application-insights-logs-and-requests).

Run them in the Azure portal under **appi-ams-api-prod → Logs** (query 8 runs in the Log Analytics workspace).
Nothing shows until the API runs with `APPLICATIONINSIGHTS_CONNECTION_STRING` set, and new data takes about
2-5 minutes to arrive.

## What a log line holds

Checked against production data (November 2026). Every log line in `traces` has these keys in `customDimensions`:

| Key | Example | Meaning |
|-----|---------|---------|
| `CategoryName` | `Infrastructure.Middleware.RequestResponseLoggingMiddleware` | The logger (class) that wrote it |
| `EventId` | `2001` | Stable id from the feature's `LogMessages` class (ranges in [CLAUDE.md](CLAUDE.md#logging)) |
| `EventName` | `ResponseSent` | Name of the log method |
| *message values* | `StatusCode`, `LocationId`, `Reason`, ... | Each `{Placeholder}` in the message, under its own name |

The queries match on `EventName`, not on message text, so rewording a message doesn't break them. Some reason codes
are part of the message text (`(reason: HttpError)`) rather than a `{Reason}` value; query 10 reads both.

A few event names are shared. `LocationAccessDenied` is used by Measurements, ElectricityCost and WaterHeater, and
`LoginFailed` by both login-failure events; filter on `CategoryName` too if you need just one.

If query 3 returns nothing, run `requests | take 10` to see the exact `name` and `url` values.

## 1. Is anything arriving?

```kql
union requests, traces, dependencies, exceptions
| where timestamp > ago(1h)
| summarize count(), latest = max(timestamp) by itemType
```

## 2. Requests per endpoint

Count, failures and 95th-percentile duration per endpoint.

```kql
requests
| where timestamp > ago(24h)
| summarize calls = count(),
            failed = countif(success == false),
            p95_ms = round(percentile(duration, 95), 0)
          by name
| order by calls desc
```

## 3. Is the meter posting every 10 s?

Expect about 30 posts per 5 minutes. Gaps mean the meter stopped sending.

```kql
requests
| where timestamp > ago(6h)
| where name startswith "POST" and url has "/api/measurements"
| summarize posts = count(), failed = countif(success == false) by bin(timestamp, 5m)
| render timechart
```

## 4. Warnings and errors from the logs

`severityLevel`: 2 = Warning, 3 = Error, 4 = Critical.

```kql
traces
| where timestamp > ago(24h)
| where severityLevel >= 2
| project timestamp, severityLevel, category = tostring(customDimensions.CategoryName), message
| order by timestamp desc
```

## 5. Exceptions

```kql
exceptions
| where timestamp > ago(7d)
| summarize count(), last = max(timestamp) by type, outerMessage
| order by count_ desc
```

## 6. Outgoing calls (ENTSO-E, Zaptec, OSO)

```kql
dependencies
| where timestamp > ago(24h)
| summarize calls = count(), failed = countif(success == false), avg_ms = round(avg(duration), 0)
          by target, resultCode
| order by calls desc
```

## 7. No database logs got through

Database logs (EF Core, Npgsql) stay in `docker logs ams-api` on the server. This should return nothing.

```kql
traces
| where timestamp > ago(24h)
| extend category = tostring(customDimensions.CategoryName)
| where category startswith "Microsoft.EntityFrameworkCore" or category startswith "Npgsql"
| take 10
```

## 8. Data volume (cost)

Run in the **Log Analytics workspace** (`log-ams-api-prod` → Logs). The first 5 GB per month are free; at one
meter post every 10 s, expect about 1.5-2 GB.

```kql
Usage
| where TimeGenerated > ago(30d)
| where IsBillable == true
| summarize GB = round(sum(Quantity) / 1024, 3) by DataType
| order by GB desc
```

## More queries

The log queries below match on `customDimensions.EventName` and read message values (`Reason`, `LocationId`,
`ActingUserId`, ...) from `customDimensions`; see [What a log line holds](#what-a-log-line-holds).

## 9. When did the meter last post?

Minutes since the last `POST /api/measurements`. Anything above 1 means the meter has stopped. This works as an
alert rule as well (Logs → New alert rule, alert when `minutes_silent > 5`); a log alert rule costs a little per
month, check the price in the portal.

```kql
requests
| where timestamp > ago(1d)
| where name startswith "POST" and url has "/api/measurements"
| summarize last_post = max(timestamp), failed_last_hour = countif(success == false and timestamp > ago(1h))
| extend minutes_silent = datetime_diff("minute", now(), last_post)
```

## 10. Background jobs: failures by reason

Water heater sampling (every 60 s), EV charger reads (every 15 min) and price fetches (every 6 h) log a stable
reason code when they fail or have nothing to do. `NoData`, `NotPublished` and `NotConfigured` are information, not
failures.

```kql
traces
| where timestamp > ago(7d)
| extend event = tostring(customDimensions.EventName)
| where event in (
    "WaterHeaterSampleFailing", "WaterHeaterSampleStillFailing", "WaterHeaterSamplingCycleFailed",
    "EvChargerReadFailing", "EvChargerReadStillFailing", "EvChargerReadCycleFailed", "EvChargerNotConfigured",
    "PriceFetchFailed", "SpotPriceTokenMissing", "SpotPriceRequestFailed", "SpotPriceInvalidResponse",
    "SpotPriceUnavailable", "SpotPriceNoData", "ExchangeRateRequestFailed", "ExchangeRateUnavailable",
    "ExchangeRateInvalidResponse", "ExchangeRateNotPublished")
| extend job = case(event startswith "WaterHeater", "WaterHeater", event startswith "EvCharger", "EvCharger", "Prices"),
         reason = coalesce(tostring(customDimensions.Reason), extract(@"reason: (\w+)", 1, message))
| summarize count(), first = min(timestamp), last = max(timestamp) by job, event, reason
| order by last desc
```

Recoveries (`WaterHeaterSampleRecovered`, `EvChargerReadRecovered`) say when a failing job started working again.

## 11. Were today's spot prices fetched?

ENTSO-E publishes the next day's prices around 13:00 CET. If `last_stored` is more than a day old, prices are missing.

```kql
traces
| where timestamp > ago(3d)
| extend what = tostring(customDimensions.EventName)
| where what in ("SpotPricesStored", "ExchangeRateStored")
| extend region = tostring(customDimensions.PriceRegion),
         new_rows = toint(customDimensions["Count"])
| summarize last_stored = max(timestamp), new_rows = sum(new_rows) by what, region
```

## 12. Failed logins and refused access

Login failures, 401/403 answers and refused requests. A burst from one account can mean someone is guessing passwords.

```kql
let logins = traces
    | where timestamp > ago(7d)
    | where tostring(customDimensions.EventName) == "LoginFailed"
    | summarize count() by kind = "LoginFailed", detail = coalesce(tostring(customDimensions.ReasonCode), "-");
let refused = traces
    | where timestamp > ago(7d)
    | extend event = tostring(customDimensions.EventName)
    | where event in ("LastAdminProtected", "UserAccessDenied", "LocationAccessDenied")
    | summarize count() by kind = event, detail = tostring(customDimensions.CategoryName);
let unauthorized = requests
    | where timestamp > ago(7d)
    | where resultCode in ("401", "403")
    | summarize count() by kind = strcat("HTTP ", resultCode), detail = name;
union logins, refused, unauthorized
| order by count_ desc
```

## 13. API restarts and deploys

The API logs its migration step on every start, so each row is one start (a deploy, a crash or a server reboot).

```kql
traces
| where timestamp > ago(30d)
| where tostring(customDimensions.EventName) in ("MigrationsStarting", "MigrationsSkipped")
| project started = timestamp, cloud_RoleInstance
| order by started desc
```

## 14. Unhandled exceptions with the request that caused them

```kql
exceptions
| where timestamp > ago(7d)
| join kind=leftouter (
    requests | where timestamp > ago(7d) | project operation_Id, request = name, resultCode, url
  ) on operation_Id
| project timestamp, type, outerMessage, request, resultCode, url, operation_Id
| order by timestamp desc
```

## 15. Everything that happened in one request

Paste an `operation_Id` from query 14 (or any other query) to see the request, its log lines, outgoing calls
and exceptions in order.

```kql
let id = "<operation_Id>";
union requests, traces, dependencies, exceptions
| where operation_Id == id
| project timestamp, itemType, name, message, resultCode, duration, severityLevel
| order by timestamp asc
```

## 16. Slowest requests

```kql
requests
| where timestamp > ago(24h)
| top 20 by duration desc
| project timestamp, name, resultCode, duration_ms = round(duration, 0), operation_Id
```

## 17. Errors over time

5xx answers and error log lines per hour, to see when something started going wrong.

```kql
let errors5xx = requests
    | where timestamp > ago(7d) and toint(resultCode) >= 500
    | summarize count() by bin(timestamp, 1h), series = "HTTP 5xx";
let errorLogs = traces
    | where timestamp > ago(7d) and severityLevel >= 3
    | summarize count() by bin(timestamp, 1h), series = "Error logs";
union errors5xx, errorLogs
| render timechart
```

## 18. Admin changes (audit trail)

Who changed what: locations, keys, users, roles, prices, tariffs and connected devices.

```kql
traces
| where timestamp > ago(30d)
| extend actor = coalesce(tostring(customDimensions.ActingUserId), tostring(customDimensions.CallerId))
| where isnotempty(actor)
| extend event = tostring(customDimensions.EventName)
// Reads and refusals carry an actor too; leave them out so only changes remain.
| where event !in ("RatesListed", "Listed", "LocationsListed", "KeyRemindersListed", "WaterHeaterStateRead",
                   "LocationAccessDenied", "UserAccessDenied", "LastAdminProtected", "RateRefused", "Refused",
                   "WaterHeaterConnectRefused", "EvChargerConnectRefused")
| project timestamp, actor, event, message
| order by timestamp desc
```

## 19. Who uses the API

Reads of measurements and cost per user and location, as a picture of how much the frontend is used.

```kql
traces
| where timestamp > ago(30d)
| where tostring(customDimensions.EventName) in ("MeasurementsQueried", "LatestMeasurementQueried", "CostQueried")
| summarize reads = count(), last = max(timestamp)
          by user = tostring(customDimensions.UserId), location = tostring(customDimensions.LocationId)
| order by reads desc
```

## 20. Which logs cost the most

Billed size per log event. Use it to decide what to filter out if the volume grows (for example the request
middleware's two lines per request, which repeat what `requests` already holds).

```kql
traces
| where timestamp > ago(7d)
| summarize MB = round(sum(_BilledSize) / 1024.0 / 1024.0, 2), lines = count()
          by category = tostring(customDimensions.CategoryName), event = tostring(customDimensions.EventName)
| order by MB desc
```

## Table names in the workspace

In the Log Analytics workspace the same data is in `AppRequests`, `AppTraces`, `AppDependencies` and
`AppExceptions`, with `TimeGenerated` instead of `timestamp`.
