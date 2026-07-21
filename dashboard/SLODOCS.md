# Candidate API SLI/SLO Documentation

## Overview

This document defines the Service Level Indicator (SLI) and Service Level Objective (SLO) for the Candidate API service.

The objective is to measure service reliability from a user perspective using request availability, latency, and saturation metrics.

---

## Custom Instrumentation Implemented for Metrics
For the purposes of this exercise, custom instrumentation was added to the Candidate API to better leverage the kube-prometheus stack.

Code for custom intrumentations can be found in:
```
src/CandidateApi/Services/CandidateApiMetrics.cs
```
| Metric | Description |
| --- | --- |
| candidate_api_readiness_status | Exposes a Gauge to assess client Readiness. 1 = Healthy, 0 = Unhealthy|
| candidate_api_dependency_status | Mock analysis of the dependencies returned by /health/ready. If these are flipped Off manually, will return error|
| candidate_api_requests_total | Count of total HTTP requests processed by the API |
| candidate_api_errors_total | Count of the total number of HTTP request failures. Used with candidate_api_requests_total to calculate availability rate|

# Service Level Objective (SLO)

## Availability SLO

The primary reliability objective for the Candidate API is maintaining high availability for consumers of the service.

### Target

| Metric | Target |
| --- | --- |
| Availability | 99.9% |
| Measurement Window | 30 days |
| Maximum Allowed Downtime | ~43 minutes/month |

A 99.9% availability target means that the service may experience up to approximately 43 minutes of downtime or failed requests within a rolling 30-day period.

---

# Service Level Indicators (SLIs)

## Availability SLI

The availability SLI measures the percentage of requests that successfully complete.

A successful request is defined as any request that does not return a server-side failure response.

### Calculation

```
Availability = Successful Requests / Total Requests * 100
```

Successful requests:

- HTTP 200
- HTTP 201
- HTTP 400
- Other non-5xx responses

Failed requests:

- HTTP 500
- HTTP 502
- HTTP 503
- Other 5xx responses

---

## Prometheus Query

The availability percentage can be calculated using:

```promql
(
  sum(rate(http_server_request_duration_seconds_count{http_response_status_code!~"5.."}[5m]))
/
  sum(rate(http_server_request_duration_seconds_count[5m]))
) * 100
```

This query calculates the percentage of successful HTTP requests over the previous five minutes.

---

# Latency SLI

## Request Duration

The Candidate API also measures request latency to ensure the service remains responsive.

The latency SLI uses the 95th percentile (p95) request duration.

### Target

| Metric | Target |
| --- | --- |
| p95 Request Latency | < 500ms |

This means that 95% of requests should complete within 500 milliseconds.

---

## Prometheus Query

The p95 latency value is calculated from the OpenTelemetry request duration histogram:

```promql
histogram_quantile(
  0.95,
  sum(rate(http_server_request_duration_seconds_bucket[5m])) by (le)
)
```

---

# Error Budget

The availability SLO determines the amount of failure that is acceptable before the service violates its reliability target.

For a 99.9% availability objective:

```
Allowed Failure Rate = 100% - 99.9%

Error Budget = 0.1%
```

Over a 30-day period:

```
30 days × 24 hours × 60 minutes × 0.1%

≈ 43 minutes of allowed downtime
```

The error budget represents the amount of reliability loss that can occur while still meeting the SLO.

---

# Error Budget Burn Rate

## Definition

Burn rate measures how quickly the service consumes its available error budget.

A burn rate of:

```
1x
```

means the service is consuming the error budget at exactly the expected rate.

Higher burn rates indicate the service is consuming the budget faster than the SLO allows.

---

# Burn Rate Alerting Strategy

The Candidate API uses multi-window, multi-burn-rate alerting to detect both severe incidents and gradual reliability degradation.

This approach separates:

- Fast burn alerts for active incidents
- Slow burn alerts for emerging reliability problems

---

# Fast Burn Alert

## Purpose

Detect severe outages or failures that rapidly consume the error budget.

## Threshold

```
Burn Rate: 14.4x
```

A 14.4x burn rate consumes the entire 30-day error budget in approximately two hours.

## Alert Condition

Trigger a critical alert when:

```
Error budget burn rate > 14.4x
```

over a short evaluation window.

Recommended windows:

```
5 minute window
AND
1 hour confirmation window
```

## Severity

Critical

## Response

Page the on-call engineer.

Examples of conditions that may trigger this alert:

- API unavailable
- Failed deployment
- Kubernetes workload failure
- Dependency outage
- Unexpected increase in HTTP 5xx responses

---

# Slow Burn Alert

## Purpose

Detect gradual degradation before the service consumes the entire error budget.

## Threshold

```
Burn Rate: 3x
```

A 3x burn rate consumes the monthly error budget in approximately 10 days.

## Alert Condition

Trigger a warning alert when:

```
Error budget burn rate > 3x
```

over a longer evaluation window.

Recommended windows:

```
6 hour window
AND
3 day confirmation window
```

## Severity

Warning

## Response

Create an operational investigation task.

Examples:

- Increasing application errors
- Performance degradation
- Resource pressure
- Dependency instability

---

# Recommended Alert Policy

| Alert Type | Burn Rate | Evaluation Window | Severity | Action |
| --- | --- | --- | --- | --- |
| Fast Burn | 14.4x | 5 minutes + 1 hour confirmation | Critical | Page on-call |
| Slow Burn | 3x | 6 hours + 3 day confirmation | Warning | Investigate trend |

---

# Saturation Metrics

Availability and latency should be combined with infrastructure saturation monitoring.

Recommended saturation signals include:

## CPU Utilization

Metric:

```
container_cpu_usage_seconds_total
```

Recommended alert:

```
CPU utilization > 80% for 15 minutes
```

Potential causes:

- Increased traffic
- Inefficient code paths
- Resource limits too low

---

## Memory Utilization

Metric:

```
container_memory_working_set_bytes
```

Recommended alert:

```
Memory utilization > 85% for 15 minutes
```

Potential causes:

- Memory leaks
- Increased workload size
- Incorrect container limits

---

## Kubernetes Availability

Monitor:

- Available pod replicas
- Pod readiness status
- Container restart count
- CrashLoopBackOff events

Recommended alerts:

```
Available replicas < desired replicas
```

or:

```
Container restart rate increasing
```

---