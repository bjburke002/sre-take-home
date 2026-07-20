using System.Diagnostics.Metrics;

namespace CandidateApi.Services;

public class CandidateApiMetrics
{
    private readonly ObservableGauge<int> _readinessStatus;
    private readonly ObservableGauge<int> _dependencyStatus;

    private int _readiness = 0;

    private readonly Dictionary<(string Name, string Type), int> _dependencies = new();

    private readonly Counter<long> _requests;
    private readonly Counter<long> _errors;


    public CandidateApiMetrics()
    {
        var meter = new Meter("CandidateApi");


        _readinessStatus = meter.CreateObservableGauge(
            "candidate_api_readiness_status",
            () => _readiness,
            description: "Current readiness state of the API. 1 = healthy, 0 = unhealthy");


        _dependencyStatus = meter.CreateObservableGauge(
            "candidate_api_dependency_status",
            () =>
            {
                return _dependencies.Select(d =>
                    new Measurement<int>(
                        d.Value,
                        new KeyValuePair<string, object?>("dependency", d.Key.Name),
                        new KeyValuePair<string, object?>("type", d.Key.Type)
                    ));
            },
            description: "Current dependency health state");
        
        _requests = meter.CreateCounter<long>(
            "candidate_api_requests_total",
            description: "Total number of HTTP requests processed");
        
        _errors = meter.CreateCounter<long>(
            "candidate_api_errors_total",
            description: "Total number of failed HTTP requests"
        );

    }


    public void SetReadiness(bool healthy)
    {
        _readiness = healthy ? 1 : 0;
    }


    public void SetDependency(
        string name,
        string type,
        bool healthy)
    {
        _dependencies[(name, type)] = healthy ? 1 : 0;
    }

    public void RecordRequest(
    string endpoint,
    int statusCode)
{
    _requests.Add(
        1,
        new KeyValuePair<string, object?>("endpoint", endpoint),
        new KeyValuePair<string, object?>("status_code", statusCode.ToString()));

    if (statusCode >= 500)
    {
        _errors.Add(
            1,
            new KeyValuePair<string, object?>("endpoint", endpoint),
            new KeyValuePair<string, object?>("status_code", statusCode.ToString()));
    }
}
}