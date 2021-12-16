using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace VeloTimerWeb.Client.Components
{
    public class CustomRemoteAuthenticatorView : RemoteAuthenticatorView
    {
        [Inject] internal IJSRuntime Js { get; set; } = null!;
        [Inject] internal NavigationManager Navigation { get; set; } = null!;

        protected override async Task OnParametersSetAsync()
        {
            if (Action == RemoteAuthenticationActions.Profile && ApplicationPaths.RemoteProfilePath != null)
            {
                UserProfile ??= LoggingIn;
            }
            else if (Action == RemoteAuthenticationActions.Register && ApplicationPaths.RemoteRegisterPath != null)
            {
                Registering ??= LoggingIn;
                await RedirectToRegister();
            }
            else
            {
                await base.OnParametersSetAsync();
            }
        }

        private ValueTask RedirectToRegister()
        {
            var loginUrl = Navigation.ToAbsoluteUri(ApplicationPaths.LogInPath).PathAndQuery;
            var registerUrl = Navigation.ToAbsoluteUri($"{ApplicationPaths.RemoteRegisterPath}?returnUrl={Uri.EscapeDataString(loginUrl)}");

            return Js.InvokeVoidAsync("location.replace", registerUrl);
        }

        private ValueTask RedirectToProfile() => Js.InvokeVoidAsync("location.replace", Navigation.ToAbsoluteUri(ApplicationPaths.RemoteProfilePath));
    }
}
