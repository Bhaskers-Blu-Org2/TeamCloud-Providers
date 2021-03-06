/**
 *  Copyright (c) Microsoft Corporation.
 *  Licensed under the MIT License.
 */

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using TeamCloud.Providers.GitHub.Services;

namespace TeamCloud.Providers.GitHub
{
    public class AppWebhookTrigger
    {
        readonly GitHubService github;

        public AppWebhookTrigger(GitHubService github)
        {
            this.github = github ?? throw new ArgumentNullException(nameof(github));
        }

        [FunctionName(nameof(AppWebhookTrigger))]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "events")] HttpRequestMessage httpRequest)
        {
            if (httpRequest is null)
                throw new ArgumentNullException(nameof(httpRequest));

            // json payload from the received webhook
            var eventType = httpRequest.GitHubEventType();
            var payload = await httpRequest.Content.ReadAsStringAsync();

            await github.HandleWebhook(eventType, payload);

            return new OkResult();
        }
    }
}
