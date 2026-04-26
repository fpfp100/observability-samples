// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.SemanticKernel;
using System;
using System.ComponentModel;

namespace Agent365SemanticKernelSampleAgent.Plugins;

/// <summary>
/// A simple Semantic Kernel plugin that provides the current date and time.
/// This plugin does not require MCP or any external services.
/// </summary>
public class DateTimePlugin
{
    [KernelFunction("get_current_datetime"), Description("Gets the current date and time in UTC and the local time zone.")]
    public string GetCurrentDateTime()
    {
        var utcNow = DateTime.UtcNow;
        var localNow = DateTime.Now;
        return $"Current UTC date/time: {utcNow:yyyy-MM-dd HH:mm:ss} | Local date/time: {localNow:yyyy-MM-dd HH:mm:ss} ({TimeZoneInfo.Local.DisplayName})";
    }
}
