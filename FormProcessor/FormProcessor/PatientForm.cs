namespace FormProcessor
{
    /// <summary>
    /// Processing statuses.
    /// </summary>
    internal class ProcessingStatuses
    {
        // Processing failed.
        public const string Failed = "Failed";

        // Processing is in progress.
        public const string Processing = "Processing";

        // Processing succeeded.
        public const string Succeeded = "Succeeded";
    }

    /// <summary>
    /// Patient form class.
    /// </summary>
    internal class PatientForm
    {
        /// <summary>
        /// URL of the blob containing the form.
        /// </summary>
        public string BlobUrl { get; set; }

        /// <summary>
        /// Id of the patient form. This id is auto-generated.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Form key/value pairs. Populated for pre-defined models.
        /// </summary>
        //public Dictionary<string, string> KeyValuePairs { get; set; } = new Dictionary<string, string>();
        public List<KeyValuePair<string, string>> KeyValuePairs { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>
        /// The type of model used to analyze the form; for custom forms,
        /// the name of the custom model used.
        /// </summary>
        public string ModelTypeProposed { get; set; }

        /// <summary>
        /// The 1-based page number in the document where the form was found.
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Patient form documents. Populated for custom models.
        /// </summary>
        public List<PatientFormDocument> PatientForms { get; set; } = new List<PatientFormDocument>();

        /// <summary>
        /// Status of the form processing.
        /// </summary>
        public string ProcessingStatus { get; set; }

        /// <summary>
        /// Form tables. Populated for pre-defined models.
        /// </summary>
        public List<List<string>> Tables { get; set; } = new List<List<string>>();

        /// <summary>
        /// Last update date/time.
        /// </summary>
        public DateTime UpdateDateTime { get; set; }
    }

    /// <summary>
    /// Patient form document.
    /// </summary>
    internal class PatientFormDocument
    {
        /// <summary>
        /// The custom fields in a document.
        /// </summary>
        public Dictionary<string, string> Fields { get; set; } = new();

        /// <summary>
        /// The type of model actually used to analyze the form, as returned in the 
        /// analyze response. For custom forms, this will be a colon-delimited string
        /// where both values are the same. For composite models, the first value will
        /// be the composite model name, and the second value will be the sub-model name.
        /// </summary>
        public string? ModelTypeActual { get; set; }

        /// <summary>
        /// The custom tables in a document. This is structured as a dictionary, where the
        /// key is the table name, and the value is a list of rows. Each row is a dictionary,
        /// where the key is the column name, and the value is the column value.
        /// </summary>
        public Dictionary<string, List<Dictionary<string, string>>> Tables { get; set; } = new();
    }
}
