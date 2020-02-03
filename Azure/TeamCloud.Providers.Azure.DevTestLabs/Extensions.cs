﻿/**
 *  Copyright (c) Microsoft Corporation.
 *  Licensed under the MIT License.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using TeamCloud.Model.Commands;

namespace TeamCloud.Providers.Azure.DevTestLabs
{
    internal static class Extensions
    {
        private static readonly int[] FinalRuntimeStatus = new int[]
        {
            (int) OrchestrationRuntimeStatus.Canceled,
            (int) OrchestrationRuntimeStatus.Completed,
            (int) OrchestrationRuntimeStatus.Terminated
        };

        internal static bool IsFinalRuntimeStatus(this DurableOrchestrationStatus status)
        {
            if (status is null) throw new ArgumentNullException(nameof(status));

            return FinalRuntimeStatus.Contains((int)status.RuntimeStatus);
        }

        internal static string GetCallbackUrl(this IHeaderDictionary headerDictionary)
        {
            return headerDictionary.TryGetValue("x-functions-callback", out var value) ? value.FirstOrDefault() : null;
        }

        internal static ICommandResult CreateResult(this ICommand command, DurableOrchestrationStatus orchestrationStatus)
        {
            var result = (orchestrationStatus.Output?.HasValues ?? false) ? orchestrationStatus.Output.ToObject<ICommandResult>() : command.CreateResult();

            result.CreatedTime = orchestrationStatus.CreatedTime;
            result.LastUpdatedTime = orchestrationStatus.LastUpdatedTime;
            result.RuntimeStatus = (CommandRuntimeStatus)orchestrationStatus.RuntimeStatus;
            result.CustomStatus = orchestrationStatus.CustomStatus?.ToString();

            return result;
        }

        internal static ICommandResult GetCommandResult(this DurableOrchestrationStatus orchestrationStatus)
        {
            if (orchestrationStatus.Output?.HasValues ?? false)
            {
                var result = orchestrationStatus.Output.ToObject<ICommandResult>();

                result.CreatedTime = orchestrationStatus.CreatedTime;
                result.LastUpdatedTime = orchestrationStatus.LastUpdatedTime;
                result.RuntimeStatus = (CommandRuntimeStatus)orchestrationStatus.RuntimeStatus;
                result.CustomStatus = orchestrationStatus.CustomStatus?.ToString();

                return result;
            }
            else if (orchestrationStatus.Input?.HasValues ?? false)
            {
                var command = orchestrationStatus.Input.ToObject<ProviderCommandMessage>()?.Command;

                return command?.CreateResult(orchestrationStatus);
            }

            return null;
        }
    }
}
