# FormProcessor Azure Function App

This project contains an Azure Function App that responds to an Event Grid blob created event, 
calls Document Intelligence to analyze the document contained in the blob, and then writes the 
results to Cosmos DB.

## Prerequisites

When deploying to Azure, the configuration settings for the Function App must contain the following:
- CosmosDB: The connection string for the Cosmos DB account.
- DocumentIntelligenceEndpoint: The endpoint for the Document Intelligence service.
- DocumentIntelligenceKey: The key for the Document Intelligence service.
- ModelType: The type of model to use for the analysis.  Valid values are:
	- GeneralDocument
	- The name of the custom or composed model to use.

The Cosmos DB database and container name are hard-coded as constants in the CosmosDBHelper class.