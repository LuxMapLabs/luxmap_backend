namespace LuxMap.Persistence.Conventions;

/// <summary>A recorded processing result/version. Corrections create a new row, never rewrite history.</summary>
public interface IImmutableRecord;
