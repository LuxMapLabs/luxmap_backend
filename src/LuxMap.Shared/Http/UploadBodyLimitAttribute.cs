using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LuxMap.Shared.Http;

/// <summary>Enforces the whole-body cap before form binding, including chunked requests.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class UploadBodyLimitAttribute(long maxBytes) : Attribute, IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        LuxMapException TooLarge() => new("UPLOAD_TOO_LARGE", HttpStatusCode.RequestEntityTooLarge, $"Request exceeds {maxBytes / (1024 * 1024)} MiB.");
        if (request.ContentLength > maxBytes) throw TooLarge();
        var original = request.Body;
        using var buffered = new MemoryStream();
        try
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await original.ReadAsync(chunk, context.HttpContext.RequestAborted)) != 0)
            {
                if (buffered.Length + read > maxBytes) throw TooLarge();
                buffered.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException e) when (e.StatusCode == 413) { throw TooLarge(); }
        buffered.Position = 0;
        request.Body = buffered;
        try { await next(); }
        finally { request.Body = original; }
    }
}
