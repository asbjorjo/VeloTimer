namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class LinkedAccountView
{
    public bool IsConnected { get; set; }
    public bool IsSocial { get; set; }
    public required string DisplayName { get; set; }
    public required string ProviderName { get; set; }
}
