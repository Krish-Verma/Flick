using System;
using System.Collections.Generic;

namespace LocalClip.Core.Models;

public class TargetWindow
{
    public ulong Hwnd { get; set; }
    public string Title { get; set; } = "";
    public uint Pid { get; set; }
    public string ExecutablePath { get; set; } = "";
    public ulong ProcessCreationFileTime { get; set; }
    public string Exe { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Exe : $"{Title} ({Exe})";
}

public class TargetMonitor
{
    public int Index { get; set; }
    public string Device { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Primary { get; set; }

    public string DisplayName => $"Display {Index + 1} ({Width}x{Height}){(Primary ? " [Primary]" : "")}";
}

public class TargetList
{
    public List<TargetWindow> Windows { get; set; } = new();
    public List<TargetMonitor> Monitors { get; set; } = new();
}
