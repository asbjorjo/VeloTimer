namespace VeloTime.WebUI.Mud.Client.ViewModel;

public class FacilityView
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public IEnumerable<InstallationView> Installations { get; set; } = Array.Empty<InstallationView>();
    public IEnumerable<Guid> LayoutId { get; set; } = Array.Empty<Guid>();
    public bool IsSelected { get; set; } = false;
}
