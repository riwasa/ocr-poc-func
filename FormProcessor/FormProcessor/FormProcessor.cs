using System.Drawing;
using System.Text.Json.Serialization;

using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FormProcessor
{
    public class FormProcessor
    {
        // Cosmos DB client. Lazy initialization is used to defer creation until first use.
        private static Lazy<CosmosClient> _lazyClient = new Lazy<CosmosClient>(InitializeCosmosClient);
        private static CosmosClient _cosmosClient => _lazyClient.Value;

        // Logger.
        private readonly ILogger _logger;

        /// <summary>
        /// Instantiates a new instance of the <see cref="FormProcessor"/> class.
        /// </summary>
        /// <param name="loggerFactory">Logger factory.</param>
        public FormProcessor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger<FormProcessor>();
        }

        /// <summary>
        /// Processes a blob that was uploaded to the intake container.
        /// </summary>
        /// <param name="cloudEvent"></param>
        /// <returns></returns>
        [Function("FormProcessor")]
        public async Task Run([EventGridTrigger] EventGridEvent cloudEvent)
        {
            _logger.LogInformation($"Processing blob: {cloudEvent.Subject}");

            // Get the blob URL from the event.
            string blobUrl = "";

            try
            {
                if (cloudEvent.EventType == SystemEventNames.StorageBlobCreated)
                {
                    StorageBlobCreatedEventData createdEvent = cloudEvent.Data.ToObjectFromJson<StorageBlobCreatedEventData>();
                    blobUrl = createdEvent.Url;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting blob URL from event");
                return;
            }

            // Create a patient form record.
            string modelType = Environment.GetEnvironmentVariable("ModelType")!;

            PatientForm patientForm = new PatientForm
            {
                BlobUrl = blobUrl,
                Id = Guid.NewGuid().ToString(),
                ModelTypeProposed = modelType,
                ProcessingStatus = ProcessingStatuses.Processing
            };

            // Determine how many pages are in the document.
            int pages = await DocumentIntelligenceHelper.AnalyzeLayout(patientForm, _logger);

            if (pages < 1)
            {
                return;
            }

            for (int page = 1; page <= pages; page++)
            {
                PatientForm patientFormPage = new PatientForm
                {
                    BlobUrl = blobUrl,
                    Id = Guid.NewGuid().ToString(),
                    ModelTypeProposed = modelType,
                    PageNumber = page,
                    ProcessingStatus = ProcessingStatuses.Processing
                };

                if (!await CosmosDBHelper.UpdatePatientForm(_cosmosClient, patientFormPage, _logger))
                {
                    return;
                }

                // Analyze the document.
                if (!await DocumentIntelligenceHelper.AnalyzeDocument(patientFormPage, _logger))
                {
                    return;
                }

                patientForm.ProcessingStatus = ProcessingStatuses.Succeeded;

                if (!await CosmosDBHelper.UpdatePatientForm(_cosmosClient, patientFormPage, _logger))
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Initializes the Cosmos DB client.
        /// </summary>
        /// <returns>Cosmos DB client.</returns>
        private static CosmosClient InitializeCosmosClient()
        {
            // Get the Cosmos DB connection string from the Function App
            // configuration settings.
            string connectionString = Environment.GetEnvironmentVariable("CosmosDB")!;

            return new CosmosClient(connectionString, new CosmosClientOptions
            {
                SerializerOptions = new CosmosSerializationOptions
                {
                    PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
                }
            });
        }
    }
}
