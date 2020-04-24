﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using TeamCloud.Model.Audit;
using TeamCloud.Model.Commands;
using TeamCloud.Model.Commands.Core;

namespace TeamCloud.Providers.Core.Activities
{
    public static class ProviderAuditActivity
    {
        [FunctionName(nameof(ProviderAuditActivity))]
        public static Task RunActivity(
            [ActivityTrigger] IDurableActivityContext functionContext,
            [DurableClient] IDurableClient durableClient,
            IBinder binder,
            ILogger log)
        {
            if (functionContext is null)
                throw new ArgumentNullException(nameof(functionContext));

            if (durableClient is null)
                throw new ArgumentNullException(nameof(durableClient));

            if (binder is null)
                throw new ArgumentNullException(nameof(binder));

            var (command, commandResult) =
                functionContext.GetInput<(ICommand, ICommandResult)>();

            try
            {
                var prefix = durableClient.GetTaskHubNameSanitized();

                return Task.WhenAll
                (
                   WriteAuditTableAsync(binder, prefix, command, commandResult),
                   WriteAuditContainerAsync(binder, prefix, command, commandResult)
                );
            }
            catch (Exception exc)
            {
                log?.LogWarning(exc, $"Failed to audit command {command?.GetType().Name ?? "UNKNOWN"} ({command?.CommandId ?? Guid.Empty})");

                return Task.CompletedTask;
            }
        }

        private static async Task WriteAuditTableAsync(IBinder binder, string prefix, ICommand command, ICommandResult commandResult)
        {
            var entity = new ProviderAuditEntity()
            {
                CommandId = command.CommandId.ToString(),
                ProjectId = command is IProviderCommand providerCommand
                            ? providerCommand.ProjectId.GetValueOrDefault().ToString()
                            : default(Guid).ToString()
            };

            var auditTable = await binder
                .BindAsync<CloudTable>(new TableAttribute($"{prefix}Audit"))
                .ConfigureAwait(false);

            var entityResult = await auditTable
                .ExecuteAsync(TableOperation.Retrieve<ProviderAuditEntity>(entity.TableEntity.PartitionKey, entity.TableEntity.RowKey))
                .ConfigureAwait(false);

            entity = entityResult.HttpStatusCode == (int)HttpStatusCode.OK
                ? (ProviderAuditEntity)entityResult.Result
                : entity;

            await auditTable
                .ExecuteAsync(TableOperation.InsertOrReplace(entity.Augment(command, commandResult)))
                .ConfigureAwait(false);
        }

        private static async Task WriteAuditContainerAsync(IBinder binder, string prefix, ICommand command, ICommandResult commandResult)
        {
            var tasks = new List<Task>()
            {
                WriteBlobAsync(command.CommandId, command)
            };

            if (commandResult != null)
            {
                tasks.Add(WriteBlobAsync(command.CommandId, commandResult));
            }

            await Task
                .WhenAll(tasks)
                .ConfigureAwait(false);

            async Task WriteBlobAsync(Guid commandId, object data)
            {
#pragma warning disable CA1308 // Normalize strings to uppercase

                var auditBlob = await binder
                    .BindAsync<CloudBlockBlob>(new BlobAttribute($"{prefix.ToLowerInvariant()}-audit/{commandId}/{data.GetType().Name}.json"))
                    .ConfigureAwait(false);

                await auditBlob
                    .UploadTextAsync(JsonConvert.SerializeObject(data, Formatting.Indented))
                    .ConfigureAwait(false);

#pragma warning restore CA1308 // Normalize strings to uppercase
            }
        }

    }
}
