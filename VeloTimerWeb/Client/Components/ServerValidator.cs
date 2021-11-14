using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VeloTimerWeb.Client.Components
{
    public class ServerValidator : ComponentBase
    {
        [CascadingParameter]
        EditContext CurrentEditContext { get; set; }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            if (this.CurrentEditContext == null)
            {
                throw new InvalidOperationException($"{nameof(ServerValidator)} requires a cascading " +
                    $"parameter of type {nameof(EditContext)}. For example, you can use {nameof(ServerValidator)} " +
                    $"inside an EditForm.");
            }
        }

        public async void Validate(HttpResponseMessage response, object model)
        {
            var messages = new ValidationMessageStore(this.CurrentEditContext);

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
            {
                var body = await response.Content.ReadAsStringAsync();
                var validationProblemDetails = JsonSerializer.Deserialize<ValidationProblemDetails>(body);

                if (validationProblemDetails.Errors != null)
                {
                    messages.Clear();

                    foreach (var error in validationProblemDetails.Errors)
                    {
                        var fieldIdentifier = new FieldIdentifier(model, error.Key);
                        messages.Add(fieldIdentifier, error.Value);
                    }
                }
            }

            CurrentEditContext.NotifyValidationStateChanged();
        }

        // This is to hold the response details when the controller returns a ValidationProblem result.
        private class ValidationProblemDetails
        {
            [JsonPropertyName("status")]
            public int? Status { get; set; }

            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("type")]
            public string Type { get; set; }

            [JsonPropertyName("errors")]
            public IDictionary<string, string[]> Errors { get; set; }
        }
    }
}
