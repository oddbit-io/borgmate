using System;

namespace BorgMate.Runtime.Progress;

/// <summary>Retry state emitted while a transient operation retry delay is active.</summary>
public sealed record RetryCountdown(int Attempt, int MaxAttempts, TimeSpan RemainingDelay);