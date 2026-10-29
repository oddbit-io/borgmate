using System;
using System.Collections.Generic;

namespace BorgMate.Infrastructure.Platform;

/// <summary>Owns the CoreFoundation objects created for one native call and releases them in reverse order.</summary>
internal class CoreFoundationScope : IDisposable
{
    private readonly List<IntPtr> _owned = [];

    /// <summary>Takes ownership of an object returned by a Create or Copy function; null references are skipped.</summary>
    public IntPtr Own(IntPtr value)
    {
        if (value != IntPtr.Zero)
            _owned.Add(value);
        return value;
    }

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
            CoreFoundationNative.CFRelease(_owned[i]);
        _owned.Clear();
    }
}
