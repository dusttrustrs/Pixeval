// Copyright (c) Pixeval.
// Licensed under the GPL-3.0 License.

using System;
using System.Threading.Tasks;

namespace Pixeval.Utilities;

/// <summary>
/// Provides a platform-registered hook for presenting the native OS share sheet for local files or directories.
/// </summary>
public static class PlatformShareHelper
{
    public static Func<string, Task<bool>>? SharePathAsync { get; set; }
}
