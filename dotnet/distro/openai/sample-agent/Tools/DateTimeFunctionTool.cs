// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;

namespace Agent365OpenAISampleAgent.Tools;

public static class DateTimeFunctionTool
{
    public static string GetDate()
    {
        return DateTimeOffset.Now.ToString("F", null);
    }
}
