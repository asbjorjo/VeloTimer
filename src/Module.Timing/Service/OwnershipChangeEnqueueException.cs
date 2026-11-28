namespace VeloTime.Module.Timing.Service;

public sealed class OwnershipChangeEnqueueException(Exception innerException)
    : Exception("Ownership changes could not be saved because their event could not be queued.", innerException);
