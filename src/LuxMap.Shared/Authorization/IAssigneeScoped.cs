using LuxMap.Shared.Contracts.Enums;

namespace LuxMap.Shared.Authorization;

public interface IAssigneeScoped
{
    string? AssignedTo { get; }
}

public interface ICurrentActorAccessor
{
    string? UserId { get; }
    UserRole? Role { get; }
}
