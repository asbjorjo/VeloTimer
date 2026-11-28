using Microsoft.AspNetCore.Components;
namespace VeloTime.WebUI.Mud.Client.Services;
public abstract class PresentationComponentBase : ComponentBase
{
    [CascadingParameter] public EffectiveDisplaySettings Display { get; set; } = EffectiveDisplaySettings.Unresolved;
}
