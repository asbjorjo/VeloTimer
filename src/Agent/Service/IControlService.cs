namespace VeloTime.Agent.Service;

public interface IControlService
{
    Task ResumeAgent();
    Task PauseAgent();
}
