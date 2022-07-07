﻿using System;
using System.Threading;
using AntiRain.IO;
using Sora;
using Sora.Command;
using Sora.Entities.Base;

namespace AntiRain;

internal static class StaticVar
{
    public static DateTime StartTime;

    public static CommandManager SoraCommandManager;

    public static QAConfigFile QaConfigFile;

    public static AutoResetEvent ServiceReady = new(false);
}