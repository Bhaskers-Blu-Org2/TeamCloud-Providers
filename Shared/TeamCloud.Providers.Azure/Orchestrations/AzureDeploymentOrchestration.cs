﻿/**
 *  Copyright (c) Microsoft Corporation.
 *  Licensed under the MIT License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using TeamCloud.Azure.Deployment;
using TeamCloud.Orchestration;
using TeamCloud.Providers.Azure.Activities;
using TeamCloud.Serialization;

namespace TeamCloud.Providers.Azure.Orchestrations
{
    public static class AzureDeploymentOrchestration
    {
        [FunctionName(nameof(AzureDeploymentOrchestration))]
        public static async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext functionContext,
            ILogger log)
        {
            if (functionContext is null)
                throw new ArgumentNullException(nameof(functionContext));

            var (deploymentActivityName, deploymentActivityInput, deploymentResourceId) = functionContext.GetInput<(string, object, string)>();

            try
            {
                if (string.IsNullOrEmpty(deploymentResourceId))
                {
                    deploymentResourceId = await functionContext
                        .CallActivityWithRetryAsync<string>(deploymentActivityName, deploymentActivityInput)
                        .ConfigureAwait(true);

                    if (string.IsNullOrEmpty(deploymentResourceId))
                    {
                        functionContext.SetOutput(null);
                    }
                    else
                    {
                        functionContext.ContinueAsNew((deploymentActivityName, deploymentActivityInput, deploymentResourceId));
                    }
                }
                else
                {
                    await functionContext
                        .CreateTimer(functionContext.CurrentUtcDateTime.AddSeconds(10), CancellationToken.None)
                        .ConfigureAwait(true);

                    var state = await functionContext
                        .CallActivityWithRetryAsync<AzureDeploymentState>(nameof(AzureDeploymentStateActivity), deploymentResourceId)
                        .ConfigureAwait(true);

                    if (state.IsProgressState())
                    {
                        functionContext.ContinueAsNew((deploymentActivityName, deploymentActivityInput, deploymentResourceId));
                    }
                    else if (state.IsErrorState())
                    {
                        var errors = await functionContext
                            .CallActivityWithRetryAsync<IEnumerable<string>>(nameof(AzureDeploymentErrorsActivity), deploymentResourceId)
                            .ConfigureAwait(true);

                        throw new AzureDeploymentException($"Deployment '{deploymentResourceId}' failed", deploymentResourceId, errors?.ToArray() ?? Array.Empty<string>());
                    }
                    else
                    {
                        var output = await functionContext
                            .CallActivityWithRetryAsync<IReadOnlyDictionary<string, object>>(nameof(AzureDeploymentOutputActivity), deploymentResourceId)
                            .ConfigureAwait(true);

                        functionContext.SetOutput(output);
                    }
                }
            }
            catch (Exception exc)
            {
                log.LogError(exc, $"Orchestration '{nameof(AzureDeploymentOrchestration)}' failed: {exc.Message}");

                throw exc.AsSerializable();
            }
        }
    }
}
