namespace VeloTime.WebUI.Mud.Client.Services;

public interface IProfileService
{
    Task<string> GetUsernameAsync();
}
