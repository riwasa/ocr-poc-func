using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace FormProcessor
{
    /// <summary>
    /// Helper methods for Cosmos DB.
    /// </summary>
    internal class CosmosDBHelper
    {
        /// <summary>
        /// Name of the Cosmos DB Database.
        /// </summary>
        private const string DatabaseName = "ocrPoc";

        /// <summary>
        /// Name of the Cosmos DB container for patient forms.
        /// </summary>
        private const string PatientFormsContainerName = "forms";

        /// <summary>
        /// Inserts or updates a patient form.
        /// </summary>
        /// <param name="cosmosClient">Cosmos DB client.</param>
        /// <param name="patientForm">Patient form.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>true if the operation succeeded; otherwise false.</returns>
        public static async Task<bool> UpdatePatientForm(CosmosClient cosmosClient, 
            PatientForm patientForm, ILogger logger)
        {
            bool success = false;

            try
            {
                patientForm.UpdateDateTime = DateTime.UtcNow;
                await cosmosClient.GetContainer(DatabaseName, PatientFormsContainerName)
                    .UpsertItemAsync(patientForm);
                success = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating patient form");
            }

            return success;
        }
    }
}
