// Copyright 2026 FINOS
// SPDX-License-Identifier: Apache-2.0
namespace Morphir.Tooling.Features.Generate;

/// <summary>Shared reservations for spill files and staged artifacts in one generation operation.</summary>
internal sealed class GenerationSessionDiskBudget(long limit)
{
    private readonly object _gate = new();
    private long _reserved;

    public long Limit { get; } = limit >= 0 ? limit : throw new ArgumentOutOfRangeException(nameof(limit));

    public bool TryReserve(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            if (bytes > Limit - _reserved) return false;
            _reserved += bytes;
            return true;
        }
    }

    public void Release(long bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        lock (_gate)
        {
            if (bytes > _reserved) throw new ArgumentOutOfRangeException(nameof(bytes));
            _reserved -= bytes;
        }
    }
}
