namespace VeloTime.Agent.Interface.Messages.Control;

public record PauseAgentCommand(string AgentId);
public record ResumeAgentCommand(string AgentId);
