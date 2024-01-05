using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
using System.Xml.Linq;

using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using Azure.AI.FormRecognizer.Models;

using Google.Protobuf.Reflection;

using Microsoft.Azure.Cosmos.Serialization.HybridRow;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PatternContexts;
using Microsoft.Extensions.Logging;

namespace FormProcessor
{
    /// <summary>
    /// Document Intelligence pre-defined model types. For custom models, 
    /// the name of the model is used.
    /// </summary>
    internal class ModelTypes
    {
        /// <summary>
        /// General document model.
        /// </summary>
        public const string GeneralDocument = "GeneralDocument";
    }

    /// <summary>
    /// Helper methods for Document Intelligence.
    /// </summary>
    internal class DocumentIntelligenceHelper
    {
        /// <summary>
        /// Analyzes a document.
        /// </summary>
        /// <param name="patientForm">Patient form.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>true if the analysis was successful; otherwise false.</returns>
        public static async Task<bool> AnalyzeDocument(PatientForm patientForm, ILogger logger)
        {
            bool success = false;

            if (string.IsNullOrWhiteSpace(patientForm.ModelTypeProposed))
            {
                logger.LogError("Model type is not set");
                return false;
            }

            if (string.Equals(patientForm.ModelTypeProposed, ModelTypes.GeneralDocument, StringComparison.OrdinalIgnoreCase))
            {
                // Use the General Document analysis model.
                success = await AnalyzeGeneral(patientForm, logger);
            }
            else
            {
                // Use a custom model.
                success = await AnalyzeCustom(patientForm, logger);
            }

            return success;
        }

        /// <summary>
        /// Analyzes a document using the pre-defined general document model.
        /// </summary>
        /// <param name="patientForm">Patient form.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>true if the analysis succeeded; otherwise false.</returns>
        public static async Task<bool> AnalyzeGeneral(PatientForm patientForm, ILogger logger)
        {
            bool success = false;

            DocumentAnalysisClient client = GetClient();

            try
            {
                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(
                    WaitUntil.Completed, "prebuilt-document", new Uri(patientForm.BlobUrl));

                AnalyzeResult result = operation.Value;

                // Get key/value pairs from the analysis result.
                if (result.KeyValuePairs != null)
                {
                    foreach (DocumentKeyValuePair kvp in result.KeyValuePairs)
                    {
                        string key = kvp.Key.Content;
                        string value = "";

                        if (kvp.Value != null && kvp.Value.Content != null)
                        {
                            value = kvp.Value.Content;
                        }

                        patientForm.KeyValuePairs.Add(new(key, value));
                    }
                }

                if (result.Tables != null)
                {
                    // Get tables from the analysis result.
                    for (int tableIndex = 0; tableIndex < result.Tables.Count; tableIndex++)
                    {
                        DocumentTable table = result.Tables[tableIndex];

                        List<string> rows = new List<string>();
                        bool firstCell = true;
                        int rowIndex = 0;
                        string row = "";

                        foreach (DocumentTableCell cell in table.Cells)
                        {
                            if (cell.RowIndex != rowIndex)
                            {
                                rows.Add(row);
                                rowIndex = cell.RowIndex;
                                row = cell.Content;
                            }
                            else
                            {
                                if (firstCell)
                                {
                                    firstCell = false;
                                    row = cell.Content;
                                }
                                else
                                {
                                    row = $"{row},{cell.Content}";
                                }
                            }
                        }

                        patientForm.Tables.Add(rows);
                    }
                }

                success = true;
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    logger.LogInformation(ex.InnerException.Message);
                }
                logger.LogError(ex, "Error analyzing document with general document model");
            }

            return success;
        }

        /// <summary>
        /// Analyzes a document using a custom model.
        /// </summary>
        /// <param name="patientForm">Patient form.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>true if the analysis succeeded; otherwise false.</returns>
        public static async Task<bool> AnalyzeCustom(PatientForm patientForm, ILogger logger)
        {
            bool success = false;

            DocumentAnalysisClient client = GetClient();

            try
            {
                logger.LogInformation("Analyzing custom form");

                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(
                    WaitUntil.Completed, patientForm.ModelTypeProposed, new Uri(patientForm.BlobUrl), 
                    new AnalyzeDocumentOptions{ Pages = { patientForm.PageNumber.ToString() } });

                AnalyzeResult result = operation.Value;
                
                logger.LogInformation("Analyzed custom form");

                // Get custom fields from the analysis result.
                foreach (AnalyzedDocument document in result.Documents)
                {
                    logger.LogInformation("Parsing document");

                    PatientFormDocument patientFormDocument = new PatientFormDocument
                    {
                        ModelTypeActual = document.DocumentType
                    };

                    foreach (KeyValuePair<string, DocumentField> fieldKvp in document.Fields)
                    {
                        string fieldName = fieldKvp.Key;
                        DocumentField field = fieldKvp.Value;

                        try
                        {
                            if (field.FieldType == DocumentFieldType.List)
                            {
                                // The field represents a custom table.
                                GetCustomTable(fieldName, field, patientFormDocument, logger);
                            }
                            else
                            {
                                // The field represents a custom field.
                                GetCustomField(fieldName, field, patientFormDocument, logger);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex.Message);
                        }
                    }

                    // Add general table fields where the content contains the value
                    // ":selected:".
                    GetGeneralTable(result, patientFormDocument, logger);

                    patientForm.PatientForms.Add(patientFormDocument);
                }

                success = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error analyzing document with custom model");
            }

            return success;
        }

        /// <summary>
        /// Analyzes a document layout.
        /// </summary>
        /// <param name="patientForm">Patient form.</param>
        /// <param name="logger">Logger.</param>
        /// <returns>Number of pages in the document.</returns>
        public static async Task<int> AnalyzeLayout(PatientForm patientForm, ILogger logger)
        {
            int pages = 0;

            DocumentAnalysisClient client = GetClient();

            try
            {
                AnalyzeDocumentOperation operation = await client.AnalyzeDocumentFromUriAsync(
                    WaitUntil.Completed, "prebuilt-layout", new Uri(patientForm.BlobUrl));

                AnalyzeResult result = operation.Value;

                if (result.Pages != null)
                {
                    pages = result.Pages.Count;
                }

            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    logger.LogInformation(ex.InnerException.Message);
                }
                logger.LogError(ex, "Error analyzing document layout");
            }

            return pages;
        }

        /// <summary>
        /// Gets a Document Intelligence document analysis client.
        /// </summary>
        /// <returns>Document Intelligence document analysis client.</returns>
        private static DocumentAnalysisClient GetClient()
        {
            string endpoint = Environment.GetEnvironmentVariable("DocumentIntelligenceEndpoint")!;
            string key = Environment.GetEnvironmentVariable("DocumentIntelligenceKey")!;

            return new DocumentAnalysisClient(new Uri(endpoint), new AzureKeyCredential(key));
        }

        /// <summary>
        /// Gets a custom field value and adds it to the patient form document.
        /// </summary>
        /// <param name="fieldName">Name of the field.</param>
        /// <param name="field">Document field.</param>
        /// <param name="patientFormDocument">Patient form document.</param>
        /// <param name="logger">Logger.</param>
        private static void GetCustomField(string fieldName, DocumentField field, PatientFormDocument patientFormDocument, ILogger logger)
        {
            string fieldValue = GetFieldValue(field);
            patientFormDocument.Fields.Add(fieldName, fieldValue);
        }

        /// <summary>
        /// Gets a custom table and adds it to the patient form document.
        /// </summary>
        /// <param name="tableName">Name of the table.</param>
        /// <param name="table">Table.</param>
        /// <param name="patientFormDocument">Patient form document.</param>
        /// <param name="logger">Logger.</param>
        private static void GetCustomTable(string tableName, DocumentField table, PatientFormDocument patientFormDocument, ILogger logger)
        {
            List<Dictionary<string, string>> tableValues = new();

            foreach (DocumentField row in table.Value.AsList())
            {
                Dictionary<string, string> rowValues = new();

                foreach (KeyValuePair<string, DocumentField> column in row.Value.AsDictionary())
                {
                    string columnName = column.Key;
                    string columnValue = GetFieldValue(column.Value);

                    rowValues.Add(columnName, columnValue);
                }

                tableValues.Add(rowValues);
            }

            patientFormDocument.Tables.Add(tableName, tableValues);
        }

        /// <summary>
        /// Gets a field value as a string.
        /// </summary>
        /// <param name="field">Document field.</param>
        /// <returns>Document field value as a string.</returns>
        private static string GetFieldValue(DocumentField field)
        {
            string fieldValue = "";

            DocumentFieldType fieldType = field.FieldType;

            switch (fieldType)
            {
                case DocumentFieldType.String:
                    fieldValue = field.Value.AsString();
                    break;
                case DocumentFieldType.Boolean:
                    fieldValue = field.Value.AsBoolean().ToString();
                    break;
                case DocumentFieldType.Double:
                    fieldValue = field.Value.AsDouble().ToString();
                    break;
                case DocumentFieldType.SelectionMark:
                    bool selected = field.Value.AsSelectionMarkState() == DocumentSelectionMarkState.Selected;
                    fieldValue = selected.ToString();
                    break;
                case DocumentFieldType.Signature:
                    bool signed = field.Value.AsSignatureType() == DocumentSignatureType.Signed;
                    fieldValue = signed.ToString();
                    break;
                case DocumentFieldType.Dictionary:
                case DocumentFieldType.List:
                    break;
                default:
                    fieldValue = field.Content;
                    break;
            }

            return fieldValue;
        }

        /// <summary>
        /// Gets a general table and adds all values containing the string ":selected:"
        /// to the patient form document.
        /// </summary>
        /// <param name="result">Result from the Document Intelligence Analyze API.</param>
        /// <param name="patientFormDocument">Patient form document.</param>
        /// <param name="logger">Logger.</param>
        private static void GetGeneralTable(AnalyzeResult result, PatientFormDocument patientFormDocument, ILogger logger)
        {
            foreach (DocumentTable table in result.Tables)
            {
                foreach (DocumentTableCell cell in table.Cells)
                {
                    if (cell.Content.Contains(":selected:"))
                    {
                        string value = cell.Content.Replace(":selected:", "");
                        patientFormDocument.Fields.Add(cell.Content, "true");
                    }
                }
            }
        }
    }
}
