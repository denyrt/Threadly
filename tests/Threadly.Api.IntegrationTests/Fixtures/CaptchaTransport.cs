using System.Net;

namespace Threadly.Api.IntegrationTests.Fixtures;

public sealed class CaptchaTransport : HttpMessageHandler
{
    private int calls;
    private int blocked;
    public int Calls => Volatile.Read(ref calls);
    public TaskCompletionSource TwoRequestsEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        string body = await request.Content!.ReadAsStringAsync(cancellationToken);
        if (body.Contains("response=block", StringComparison.Ordinal))
        {
            if (Interlocked.Increment(ref blocked) == 2) TwoRequestsEntered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
        }
        if (body.Contains("response=unavailable", StringComparison.Ordinal))
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        bool valid = body.Contains("response=test-token", StringComparison.Ordinal);
        string json = valid
            ? "{\"success\":true,\"hostname\":\"localhost\",\"action\":\"comment_create\"}"
            : "{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
    }
}
