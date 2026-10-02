namespace LuxMap.Shared.Http;

public enum ClientSurface
{
    Mobile,
    Web,
}

/// <summary>Documents a client-specific endpoint. Unmarked endpoints are shared.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ClientSurfaceAttribute(ClientSurface surface) : Attribute
{
    public ClientSurface Surface { get; } = surface;
}
