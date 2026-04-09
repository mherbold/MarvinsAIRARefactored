
using System.Diagnostics;

namespace MarvinsAIRARefactored.Classes;

[Serializable]
public class AppManagerStartEntry
{
	public string ExecutablePath { get; set; } = string.Empty;
	public string Arguments { get; set; } = string.Empty;
	public float DelayAfterStartSeconds { get; set; } = 0f;
	public ProcessPriorityClass CpuPriority { get; set; } = ProcessPriorityClass.Normal;
	public ProcessWindowStyle WindowStyle { get; set; } = ProcessWindowStyle.Normal;
	public bool AvoidPrimaryCpuCores { get; set; } = false;
	public bool TerminateOnSimulatorDisconnect { get; set; } = false;
}

[Serializable]
public class AppManagerTerminateEntry
{
	public string ExecutablePath { get; set; } = string.Empty;
	public bool LaunchOnSimulatorDisconnect { get; set; } = false;
}
