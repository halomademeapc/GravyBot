﻿using System;

namespace GravyBot.Commands
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ChannelOnlyAttribute : Attribute { }
}